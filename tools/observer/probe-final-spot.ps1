param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][string]$CheckpointPath
)

$ErrorActionPreference = 'Stop'
$expectedHash = 'D9B92CA9FF84302D6EEF31FDE2074A0CA027D51C5F33AAF290EE0DD1A1484CCB'
$checkpoint = Get-Content -LiteralPath $CheckpointPath -Raw | ConvertFrom-Json
if ($checkpoint.track.name -ne 'Circuit de Barcelona') { throw 'Checkpoint is not for Barcelona.' }
$position = $checkpoint.pose.position
$target = @([single]$position.x, [single]$position.y, [single]$position.z)
if (@($target | Where-Object { [single]::IsNaN($_) -or [single]::IsInfinity($_) }).Count) {
    throw 'Checkpoint position is not finite.'
}
$game = Get-Process -Id $ProcessId
if ($game.ProcessName -ne 'Le Mans Ultimate' -or
    (Get-FileHash -LiteralPath $game.MainModule.FileName -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Wrong process or executable build.'
}
if (Get-Process | Where-Object { $_.ProcessName -match 'EasyAnti|start_protected' }) {
    throw 'Protected launch detected.'
}
$loaded = @($game.Modules | Where-Object { $_.ModuleName -match '^lmu_.*_observer\.dll$' })
if ($loaded.Count -ne 1 -or $loaded[0].ModuleName -ne 'lmu_final_spot_observer.dll') {
    throw 'Expected exactly the final-spot observer in the process.'
}
$log = Join-Path $PSScriptRoot 'build-pit4/lmu_final_spot_observer.log'
$priorCalls = @(Get-Content -LiteralPath $log | Where-Object { $_ -match '^drive_spot=' }).Count
if ($priorCalls -lt 1) { throw 'No baseline Drive call was captured.' }

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SpotNative {
    [StructLayout(LayoutKind.Sequential)] public struct Mbi {
        public IntPtr BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public ushort Pad;
        public IntPtr RegionSize;
        public uint State, Protect, Type;
    }
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int n, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool WriteProcessMemory(IntPtr h, IntPtr a, byte[] b, int n, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr VirtualQueryEx(IntPtr h, IntPtr a, out Mbi info, IntPtr size);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
'@

$handle = [SpotNative]::OpenProcess(0x438, $false, $ProcessId) # query, read, write, VM operation
if ($handle -eq [IntPtr]::Zero) { throw 'Cannot open game memory.' }
$entry = [int64]0
$original = $null
$modified = $false
function Read-Exact([int64]$address, [int]$length) {
    $bytes = New-Object byte[] $length
    $count = [IntPtr]::Zero
    if (-not [SpotNative]::ReadProcessMemory($handle, [IntPtr]$address, $bytes, $length, [ref]$count) -or
        $count.ToInt64() -ne $length) { throw ('Read failed at 0x{0:X}' -f $address) }
    return ,$bytes
}
function Write-Verified([int64]$address, [byte[]]$bytes) {
    $count = [IntPtr]::Zero
    if (-not [SpotNative]::WriteProcessMemory($handle, [IntPtr]$address, $bytes, $bytes.Length, [ref]$count) -or
        $count.ToInt64() -ne $bytes.Length) { throw ('Write failed at 0x{0:X}' -f $address) }
    if ([BitConverter]::ToString((Read-Exact $address $bytes.Length)) -ne
        [BitConverter]::ToString($bytes)) {
        throw 'Write read-back mismatch.'
    }
}
try {
    $base = [int64]$game.MainModule.BaseAddress
    $pointerAddress = $base + 0x01DFA5D8
    $entry = [BitConverter]::ToInt64((Read-Exact $pointerAddress 8), 0)
    if ($entry -eq 0 -or $entry % 4 -ne 0) { throw 'Invalid entry pointer.' }
    $obj = $base + 0x01E16170
    foreach ($offset in @(0x90B0,0x471D4,0x471E8,0x471EC)) {
        if ([BitConverter]::ToInt32((Read-Exact ($obj + $offset) 4),0) -ne 0) {
            throw ('Slot selector +0x{0:X} changed.' -f $offset)
        }
    }
    $info = New-Object SpotNative+Mbi
    if ([SpotNative]::VirtualQueryEx($handle, [IntPtr]$entry, [ref]$info,
        [IntPtr][Runtime.InteropServices.Marshal]::SizeOf([type][SpotNative+Mbi])) -eq [IntPtr]::Zero -or
        $info.State -ne 0x1000 -or $info.Protect -notin @(4,8) -or
        $entry + 24 -gt $info.BaseAddress.ToInt64() + $info.RegionSize.ToInt64()) {
        throw 'Spot entry is not wholly inside a committed writable data page.'
    }
    $original = Read-Exact $entry 24
    $old = @(0..2 | ForEach-Object { [BitConverter]::ToSingle($original,$_ * 4) })
    if ([Math]::Abs($old[0] - 138.939) -gt 0.001 -or
        [Math]::Abs($old[1] - 0.493) -gt 0.001 -or
        [Math]::Abs($old[2] - 245.873) -gt 0.001) { throw 'Unexpected entry position.' }
    $distance = [Math]::Sqrt([Math]::Pow($target[0]-$old[0],2)+[Math]::Pow($target[2]-$old[2],2))
    if ($distance -lt 10 -or $distance -gt 1000) { throw "Checkpoint horizontal distance $distance outside test range." }
    $payload = [byte[]]$original.Clone()
    for($axis=0;$axis -lt 3;$axis++){ [BitConverter]::GetBytes($target[$axis]).CopyTo($payload,$axis*4) }
    $modified = $true # Treat a partial write as modified too.
    Write-Verified $entry $payload
    Write-Output ('ARMED: entry 0 position ({0:F3},{1:F3},{2:F3}) -> ({3:F3},{4:F3},{5:F3}); original orientation retained; press Drive once (180-second timeout).' -f $old[0],$old[1],$old[2],$target[0],$target[1],$target[2])
    $deadline = [DateTime]::UtcNow.AddSeconds(180)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) { throw 'LMU exited.' }
        $calls = @(Get-Content -LiteralPath $log | Where-Object { $_ -match '^drive_spot=' })
        if ($calls.Count -gt $priorCalls) { Write-Output $calls[-1]; break }
        Start-Sleep -Milliseconds 100
    }
    if ($calls.Count -le $priorCalls) { throw 'Timed out without another Drive lookup.' }
}
finally {
    try {
        if ($modified -and $original) {
            if ([BitConverter]::ToInt64((Read-Exact $pointerAddress 8),0) -ne $entry) {
                throw 'Table pointer moved; old entry was not touched again.'
            }
            Write-Verified $entry $original
            Write-Output 'RESTORED: original 24 bytes verified.'
        }
    } finally { [SpotNative]::CloseHandle($handle) | Out-Null }
}

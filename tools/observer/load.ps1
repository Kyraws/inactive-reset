param(
    [Parameter(Mandatory)] [int]$ProcessId,
    [string]$Dll = (Join-Path $PSScriptRoot 'build-next/lmu_spot_observer.dll')
)

$ErrorActionPreference = 'Stop'
$expectedHash = 'D9B92CA9FF84302D6EEF31FDE2074A0CA027D51C5F33AAF290EE0DD1A1484CCB'
$game = Get-Process -Id $ProcessId
if ($game.ProcessName -ne 'Le Mans Ultimate') { throw 'The PID is not Le Mans Ultimate.' }
if (Get-Process | Where-Object { $_.ProcessName -match 'EasyAnti|start_protected' }) {
    throw 'Protected launch detected. The observer is direct-launch only.'
}
if ($game.Modules | Where-Object { $_.ModuleName -match '^EasyAnti|^EAC' }) {
    throw 'Anticheat module detected in LMU. Observer refused.'
}
$image = $game.MainModule.FileName
if ([IO.Path]::GetFileName($image) -ne 'Le Mans Ultimate.exe') { throw 'Unexpected game image.' }
if ((Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash -ne $expectedHash) {
    Write-Warning 'Unknown LMU build: the DLL will hook only if its unique structural signature validates.'
}
$Dll = (Resolve-Path -LiteralPath $Dll).Path
if ($game.Modules | Where-Object { $_.ModuleName -match '^lmu_(spot|flag|pit|pit_lookup|drive|final_transform|final_spot)_observer\.dll$' }) {
    throw 'An LMU observer is already loaded. Exit LMU before loading another one.'
}
$logPath = Join-Path (Split-Path $Dll) ([IO.Path]::GetFileNameWithoutExtension($Dll) + '.log')
$oldLength = if (Test-Path -LiteralPath $logPath) { (Get-Item -LiteralPath $logPath).Length } else { 0 }

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Native {
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] data, UIntPtr size, out UIntPtr written);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string name);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GetModuleHandleExW(uint flags, IntPtr address, out IntPtr module);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, UIntPtr stackSize, IntPtr start, IntPtr parameter, uint flags, out uint threadId);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GetExitCodeThread(IntPtr thread, out uint code);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool CloseHandle(IntPtr handle);
}
'@

# Resolve LoadLibraryW in the remote process's copy of its actual containing module.
$localKernel = [Native]::GetModuleHandleW('kernel32.dll')
$localFunction = [Native]::GetProcAddress($localKernel, 'LoadLibraryW')
$localModule = [IntPtr]::Zero
if ($localFunction -eq [IntPtr]::Zero -or -not [Native]::GetModuleHandleExW(4, $localFunction, [ref]$localModule)) {
    throw 'Cannot resolve local LoadLibraryW.'
}
$localName = [Diagnostics.Process]::GetCurrentProcess().Modules |
    Where-Object { $_.BaseAddress -eq $localModule } | Select-Object -First 1 -ExpandProperty ModuleName
$remoteModule = $game.Modules | Where-Object { $_.ModuleName -eq $localName } | Select-Object -First 1
if (-not $remoteModule) { throw "The game does not have $localName loaded." }
$remoteFunction = [IntPtr]::new($remoteModule.BaseAddress.ToInt64() + $localFunction.ToInt64() - $localModule.ToInt64())

$bytes = [Text.Encoding]::Unicode.GetBytes($Dll + [char]0)
$handle = [Native]::OpenProcess(0x43A, $false, $ProcessId) # CREATE_THREAD|QUERY_INFORMATION|VM_OPERATION|VM_WRITE|VM_READ
if ($handle -eq [IntPtr]::Zero) { throw "OpenProcess failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
$remotePath = [IntPtr]::Zero
$thread = [IntPtr]::Zero
try {
    $remotePath = [Native]::VirtualAllocEx($handle, [IntPtr]::Zero, [UIntPtr]::new($bytes.Length), 0x3000, 4)
    if ($remotePath -eq [IntPtr]::Zero) { throw 'VirtualAllocEx failed.' }
    $written = [UIntPtr]::Zero
    if (-not [Native]::WriteProcessMemory($handle, $remotePath, $bytes, [UIntPtr]::new($bytes.Length), [ref]$written) -or
        $written.ToUInt64() -ne $bytes.Length) { throw 'WriteProcessMemory failed.' }
    $threadId = [uint32]0
    $thread = [Native]::CreateRemoteThread($handle, [IntPtr]::Zero, [UIntPtr]::Zero, $remoteFunction, $remotePath, 0, [ref]$threadId)
    if ($thread -eq [IntPtr]::Zero) { throw 'CreateRemoteThread failed.' }
    $wait = [Native]::WaitForSingleObject($thread, 10000)
    if ($wait -ne 0) { throw "LoadLibraryW did not complete (wait result $wait); remote path retained." }
    $code = [uint32]0
    if (-not [Native]::GetExitCodeThread($thread, [ref]$code) -or $code -eq 0) {
        throw 'LoadLibraryW failed in the game process.'
    }
    $status = $null
    for ($i = 0; $i -lt 50; $i++) {
        Start-Sleep -Milliseconds 100
        if ((Test-Path -LiteralPath $logPath) -and (Get-Item -LiteralPath $logPath).Length -gt $oldLength) {
            $newText = (Get-Content -LiteralPath $logPath -Raw).Substring([int]$oldLength)
            $match = [regex]::Match($newText, '(?m)^(ACTIVE|REFUSED):[^\r\n]*')
            if ($match.Success) { $status = $match.Value; break }
        }
    }
    if ($status -notmatch '^(ACTIVE|REFUSED):') { throw "DLL loaded but no hook status appeared in $logPath." }
    if ($status -match '^REFUSED:') { throw $status }
    Write-Output "$status (PID $ProcessId; log: $logPath)"
} finally {
    if ($thread -ne [IntPtr]::Zero) { [void][Native]::CloseHandle($thread) }
    if ($remotePath -ne [IntPtr]::Zero -and $wait -eq 0) {
        [void][Native]::VirtualFreeEx($handle, $remotePath, [UIntPtr]::Zero, 0x8000)
    }
    [void][Native]::CloseHandle($handle)
}

param(
    [Parameter(Mandatory)] [string]$Dump,
    [Parameter(Mandatory)] [string]$Seeds,
    [Parameter(Mandatory)] [string]$Output,
    [string]$GhidraHome = 'G:\Tools\Ghidra\ghidra_12.1.2_PUBLIC',
    [string]$JavaHome
)

$ErrorActionPreference = 'Stop'
$dumpPath = (Resolve-Path -LiteralPath $Dump).Path
$seedPath = (Resolve-Path -LiteralPath $Seeds).Path
$headless = Join-Path $GhidraHome 'support\analyzeHeadless.bat'
if (-not (Test-Path -LiteralPath $headless)) { throw "Ghidra not found: $headless" }
if ($JavaHome) { $env:JAVA_HOME = (Resolve-Path -LiteralPath $JavaHome).Path }

$sites = @(Get-Content -LiteralPath $seedPath | Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith('#') })
if ($sites.Count -eq 0) { throw 'Seed manifest has no readers.' }
foreach ($site in $sites) {
    if (($site.Split('|')).Count -ne 3) { throw "Invalid seed: $site" }
}

$outPath = [System.IO.Path]::GetFullPath($Output)
$outDir = Split-Path -Parent $outPath
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$projectDir = Join-Path $outDir 'ghidra-projects'
New-Item -ItemType Directory -Force -Path $projectDir | Out-Null
$hash = (Get-FileHash -LiteralPath $dumpPath -Algorithm SHA256).Hash
$projectName = "Evidence_$($hash.Substring(0, 12))_$([System.IO.Path]::GetFileNameWithoutExtension($dumpPath))"
$projectFile = Join-Path $projectDir "$projectName.gpr"
$source = if (Test-Path -LiteralPath $projectFile) {
    @('-process', [System.IO.Path]::GetFileName($dumpPath))
} else {
    @('-import', $dumpPath, '-loader', 'BinaryLoader', '-loader-baseAddr', '0',
      '-processor', 'x86:LE:64:default', '-cspec', 'windows')
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$startedUtc = (Get-Date).ToUniversalTime()
& $headless $projectDir $projectName @source -noanalysis -scriptPath $scriptDir `
    -postScript ExportReaderEvidence.java $outPath @sites
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outPath)) {
    throw "Ghidra evidence export failed (exit $LASTEXITCODE)."
}
if ((Get-Item -LiteralPath $outPath).LastWriteTimeUtc -lt $startedUtc) {
    throw 'Ghidra did not refresh the evidence output.'
}

$report = Get-Content -LiteralPath $outPath -Raw | ConvertFrom-Json
if ($report.inputSha256 -ne $hash) { throw 'Evidence input hash does not match dump.' }
if ($report.sites.Count -ne $sites.Count) { throw 'Evidence output does not match seed manifest.' }
$report.sites | Format-Table label, status, readerRva, instruction -AutoSize
if (@($report.sites | Where-Object { $_.status -notin @('direct-reference', 'context-only', 'unresolved') }).Count -gt 0) {
    throw 'Some readers did not directly reference their claimed data RVA; see evidence JSON.'
}
Write-Host "Evidence: $outPath"

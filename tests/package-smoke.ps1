[CmdletBinding()]
param([Parameter(Mandatory)][string] $Zip)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$zipPath = (Resolve-Path -LiteralPath $Zip).Path
$repository = Split-Path $PSScriptRoot -Parent
$output = Join-Path $repository 'artifacts/TestResults/package'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$smokeRoot = Join-Path $temporaryBase ('inactive-reset-package-' + [guid]::NewGuid().ToString('N'))
$server = $null
$client = $null

function Start-Cli([string[]] $Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = Join-Path $smokeRoot 'inactive-reset.exe'
    $info.WorkingDirectory = $smokeRoot
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    # These arguments are only fixed flags and a numeric port, with no shell involved.
    $info.Arguments = $Arguments -join ' '
    return [Diagnostics.Process]::Start($info)
}

try {
    Expand-Archive -LiteralPath $zipPath -DestinationPath $smokeRoot
    foreach ($relative in 'inactive-reset.exe','inactive-reset-ui.exe','offsets/shared-memory.json','data/profiles-default',
        'README.md','docs/CONTRIBUTING.md','docs/CONTEXT.md','docs/research/README.md') {
        if (!(Test-Path -LiteralPath (Join-Path $smokeRoot $relative))) { throw "Package is missing $relative" }
    }
    $layout = Get-Content -Raw -LiteralPath (Join-Path $smokeRoot 'offsets/shared-memory.json') | ConvertFrom-Json
    if ($layout.layoutSize -le 0) { throw 'Packaged SDK layout is invalid.' }
    if (Test-Path -LiteralPath (Join-Path $smokeRoot 'data/checkpoints')) { throw 'Package includes user checkpoints.' }
    if (Get-ChildItem -LiteralPath (Join-Path $smokeRoot 'offsets') -Filter '*.auto.json') { throw 'Package includes local build caches.' }
    foreach ($document in Get-ChildItem -LiteralPath $smokeRoot -Recurse -File -Filter *.md) {
        $markdown = [IO.File]::ReadAllText($document.FullName)
        foreach ($link in [regex]::Matches($markdown, '\]\(([^\s)]+)\)')) {
            $target = $link.Groups[1].Value.Split('#')[0]
            if (!$target -or $target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:') { continue }
            $path = Join-Path $document.DirectoryName ([Uri]::UnescapeDataString($target))
            if (!(Test-Path -LiteralPath $path)) { throw "Broken packaged documentation link: $($document.Name) -> $target" }
        }
    }

    foreach ($arguments in @(@('--help'), @('list'))) {
        $process = Start-Cli $arguments
        try {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (!$process.WaitForExit(15000)) { $process.Kill(); throw 'Packaged CLI startup timed out.' }
            if ($process.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($stdout.GetAwaiter().GetResult())) {
                throw ('Packaged CLI failed: ' + $stderr.GetAwaiter().GetResult())
            }
        } finally { $process.Dispose() }
    }

    $port = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $port.Start(); $number = ([Net.IPEndPoint]$port.LocalEndpoint).Port; $port.Stop()
    $server = Start-Cli @('serve','--no-open','--port',"$number")
    $serverOutput = $server.StandardOutput.ReadToEndAsync()
    $serverError = $server.StandardError.ReadToEndAsync()
    $handler = [Net.Http.HttpClientHandler]::new(); $handler.UseProxy = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(2)
    $base = "http://127.0.0.1:$number/"
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $html = $null
    while (!$html) {
        if ($server.HasExited) { throw ('Packaged server exited: ' + $serverError.GetAwaiter().GetResult()) }
        try { $html = $client.GetStringAsync($base).GetAwaiter().GetResult() }
        catch { if ([DateTime]::UtcNow -ge $deadline) { throw }; Start-Sleep -Milliseconds 100 }
    }
    if ($html -notmatch '<!doctype\s+html|<html\b|<head\b|<body\b' -or $html -notmatch '<script\b') {
        Set-Content -Encoding UTF8 -LiteralPath (Join-Path $output 'unexpected-page.html') -Value $html
        throw 'Packaged server did not serve an application page with scripts.'
    }
    foreach ($match in [regex]::Matches($html, '(?:src|href)="(/[^\"]+)"')) {
        $asset = $client.GetStringAsync($base.TrimEnd('/') + $match.Groups[1].Value).GetAwaiter().GetResult()
        if ([string]::IsNullOrWhiteSpace($asset)) { throw ('Empty packaged asset: ' + $match.Groups[1].Value) }
    }
    @{passed=$true;checks=@('required files','SDK layout','no user data/caches','documentation links','CLI help/list','HTTP startup','embedded page/assets')} |
        ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $output 'results.json')
    Write-Host 'Package smoke passed from an extracted ZIP outside the repository.'
} catch {
    @{passed=$false;error=$_.Exception.Message} | ConvertTo-Json |
        Set-Content -Encoding UTF8 -LiteralPath (Join-Path $output 'results.json')
    throw
} finally {
    if ($client) { $client.Dispose() }
    if ($server) {
        if (!$server.HasExited) { $server.Kill(); $server.WaitForExit(5000) | Out-Null }
        $server.Dispose()
    }
    $resolvedSmoke = [IO.Path]::GetFullPath($smokeRoot)
    if (!$resolvedSmoke.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedSmoke) -notlike 'inactive-reset-package-*') { throw 'Unsafe temporary cleanup path.' }
    if (Test-Path -LiteralPath $resolvedSmoke) { Remove-Item -LiteralPath $resolvedSmoke -Recurse -Force }
}

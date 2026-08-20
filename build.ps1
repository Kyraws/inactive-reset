<#
.SYNOPSIS
    Build, test, ship, and clean Inactive Reset.

.EXAMPLE
    .\build.ps1              # build (Debug)
    .\build.ps1 test         # build + run tests
    .\build.ps1 ship         # Release single-file app + CLI into dist\
    .\build.ps1 clean        # delete artifacts\ and dist\
#>
[CmdletBinding()]
param(
    [ValidateSet('build', 'test', 'ship', 'clean')]
    [string] $Task = 'build',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root      = $PSScriptRoot
$solution  = Join-Path $root 'InactiveReset.sln'
$artifacts = Join-Path $root 'artifacts'
$dist      = Join-Path $root 'dist'

function Invoke-Step {
    param([string] $Name, [scriptblock] $Body)
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Body
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit $LASTEXITCODE)" }
}

<#
Report where a build put its executables.

Nothing lands beside its project any more: Directory.Build.props sets
UseArtifactsOutput, so everything goes under artifacts\bin\<project>\<config>,
where <config> gains a _win-x64 suffix for the two self-contained apps. These
are runnable but NOT self-contained single files -- that is what `ship` makes.
#>
function Show-BuildOutput {
    param([string] $Configuration)

    $config = $Configuration.ToLowerInvariant()
    Write-Host ''
    Write-Host "Built to artifacts\bin\ ($config):" -ForegroundColor Green

    $found = $false
    foreach ($name in 'inactive-reset-ui', 'inactive-reset') {
        $exe = Get-ChildItem (Join-Path $artifacts 'bin') -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -like "$config*" } |
            Select-Object -First 1
        if ($exe) {
            $found = $true
            $label = if ($name -like '*-ui') { 'window' } else { 'console' }
            Write-Host ('  {0,-8} {1}' -f $label, $exe.FullName.Replace("$root\", ''))
        }
    }
    if (-not $found) {
        Write-Host '  (libraries only -- no executable projects built)' -ForegroundColor DarkGray
    }

    Write-Host ''
    Write-Host '  Run without publishing:' -ForegroundColor DarkGray
    Write-Host '    dotnet run --project src\InactiveReset.Cli -- status' -ForegroundColor DarkGray
    Write-Host '    dotnet run --project src\InactiveReset.App' -ForegroundColor DarkGray
    Write-Host '  Standalone single-file builds:  .\build ship' -ForegroundColor DarkGray
}

switch ($Task) {

    'build' {
        # Always build the solution, never a single .csproj: the projects are
        # x64-only and the .sln supplies the Any CPU -> x64 mapping.
        Invoke-Step "build ($Configuration)" { dotnet build $solution -c $Configuration --nologo }
        Show-BuildOutput $Configuration
    }

    'test' {
        Invoke-Step "test ($Configuration)" { dotnet test $solution -c $Configuration --nologo }
        Show-BuildOutput $Configuration
    }

    'ship' {
        # Release always. Debug disables inlining and changes floating-point
        # codegen, which the placement math is measured against.

        # A published exe still running holds a lock on its own file. Say so
        # plainly rather than failing with an access-denied stack trace, and
        # never kill it here: the app may be mid-placement, holding original
        # spot-table bytes that it still has to restore.
        $running = Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($dist, [StringComparison]::OrdinalIgnoreCase) }
        if ($running) {
            $running | ForEach-Object { Write-Host "  running: $($_.ProcessName) (pid $($_.Id))" -ForegroundColor Yellow }
            throw 'Close the running app before shipping.'
        }

        if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

        Invoke-Step 'test (Release)' { dotnet test $solution -c Release --nologo }

        foreach ($project in 'src\InactiveReset.App', 'src\InactiveReset.Cli') {
            Invoke-Step "publish $project" {
                dotnet publish (Join-Path $root $project) -c Release -o $dist --nologo
            }
        }

        # PDBs are debug symbols, not shipping artifacts. Keep them out of the
        # release so dist\ is exactly what a user needs.
        Get-ChildItem $dist -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
        Get-ChildItem $dist -Filter *.xml -ErrorAction SilentlyContinue | Remove-Item -Force

        $purpose = @{
            'inactive-reset-ui.exe' = 'double-click; WebView2 window'
            'inactive-reset.exe'    = 'console; run --help for usage'
        }

        Write-Host ''
        Write-Host "Shipped to $dist" -ForegroundColor Green
        Get-ChildItem $dist -File | Sort-Object Name | ForEach-Object {
            $what = $purpose[$_.Name]
            if (-not $what) { $what = '' }
            Write-Host ('  {0,-24} {1,7:N1} MB   {2}' -f $_.Name, ($_.Length / 1MB), $what)
        }

        Write-Host ''
        Write-Host '  Self-contained: no .NET runtime needed on the target machine.' -ForegroundColor DarkGray
        Write-Host '  Both read offsets\ and data\ by searching upwards from the exe,' -ForegroundColor DarkGray
        Write-Host '  so keep dist\ inside the repository or pass --offsets / --data.' -ForegroundColor DarkGray
    }

    'clean' {
        # dotnet clean leaves the intermediate output behind; deleting the
        # directories is the only complete clean.
        foreach ($path in $artifacts, $dist) {
            if (Test-Path $path) {
                Write-Host "==> removing $path" -ForegroundColor Cyan
                Remove-Item $path -Recurse -Force
            }
        }

        # Strays from before the artifacts\ layout, if any survive.
        Get-ChildItem $root -Include bin, obj -Recurse -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { Write-Host "==> removing $($_.FullName)"; Remove-Item $_.FullName -Recurse -Force }

        Write-Host 'Clean.' -ForegroundColor Green
    }
}

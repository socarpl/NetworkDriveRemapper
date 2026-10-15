<#
.SYNOPSIS
Builds and verifies NetworkDriveRemapper, with optional UI renders and a distributable ZIP.
.DESCRIPTION
Uses repository-local CLI state and disables optional SDK workload discovery. The solution has no
third-party packages. Tests use fake mappings and a read-only real Windows inventory; no real drive
is mapped or disconnected. Native UI tests require an interactive Windows desktop session.
.PARAMETER RenderUi
Saves offscreen WinForms renders under artifacts/ui for visual inspection.
.PARAMETER Publish
Publishes a framework-dependent application folder and ZIP under artifacts. The target computer
must have the .NET 8 Desktop Runtime matching the executable architecture (x64 on the supplied build).
.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -RenderUi -Publish
#>
[CmdletBinding()]
param(
    [switch] $RenderUi,
    [switch] $Publish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$previousCliHome = $env:DOTNET_CLI_HOME
$previousResolver = $env:MSBuildEnableWorkloadResolver
$previousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT

function Invoke-DotNet {
    <#
    .SYNOPSIS
    Executes dotnet with separate argument values and turns a nonzero native exit code into an error.
    .PARAMETER Arguments
    Exact argument array forwarded without shell-string evaluation or command construction.
    #>
    param([Parameter(Mandatory = $true)] [string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

Push-Location -LiteralPath $repoRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet-home'
    $env:MSBuildEnableWorkloadResolver = 'false'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    Invoke-DotNet -Arguments @('build', 'NetworkDriveRemapper.sln', '-c', 'Release', '--ignore-failed-sources')
    $testArguments = @('run', '--project', 'tests/NetworkDriveRemapper.Tests', '-c', 'Release', '--no-build')
    if ($RenderUi) { $testArguments += @('--', '--render') }
    Invoke-DotNet -Arguments $testArguments

    if ($Publish) {
        $publishPath = Join-Path $repoRoot 'artifacts\publish'
        Invoke-DotNet -Arguments @('publish', 'src/NetworkDriveRemapper/NetworkDriveRemapper.csproj',
            '-c', 'Release', '--no-restore', '-o', $publishPath)
        Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $publishPath -Force
        $publishedDocs = Join-Path $publishPath 'docs'
        New-Item -Path $publishedDocs -ItemType Directory -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\DEVELOPMENT.md') -Destination $publishedDocs -Force
        $zipPath = Join-Path $repoRoot 'artifacts\NetworkDriveRemapper.zip'
        Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $zipPath -Force
        Write-Output "Application: $publishPath\NetworkDriveRemapper.exe"
        Write-Output "Package: $repoRoot\artifacts\NetworkDriveRemapper.zip"
    }
}
finally {
    # Preserve the caller's working directory and process environment even if a build/test fails.
    $env:DOTNET_CLI_HOME = $previousCliHome
    $env:MSBuildEnableWorkloadResolver = $previousResolver
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetry
    Pop-Location
}

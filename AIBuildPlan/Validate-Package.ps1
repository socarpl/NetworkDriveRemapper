<#
.SYNOPSIS
Validates the portable reconstruction package without changing files or accessing network shares.
.DESCRIPTION
Checks manifest hashes/lengths, paths, file inventory, the 33-test index, archive baseline contents,
copied test/assets agreement, exact help paragraphs, and local Markdown file links.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$packageRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$packagePrefix = $packageRoot + [System.IO.Path]::DirectorySeparatorChar
$manifestPath = Join-Path $packageRoot 'MANIFEST.json'

function Resolve-PackagePath {
    <#
    .SYNOPSIS
    Resolves a relative package path and rejects rooted or escaping paths.
    .PARAMETER RelativePath
    Manifest or Markdown target relative to the package root.
    #>
    param([Parameter(Mandatory = $true)][string] $RelativePath)
    if ([System.IO.Path]::IsPathRooted($RelativePath)) { throw "Rooted package path: $RelativePath" }
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $packageRoot $RelativePath))
    if (-not $resolved.StartsWith($packagePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes the package: $RelativePath"
    }
    return $resolved
}

function Read-ArchiveText {
    <#
    .SYNOPSIS
    Reads a UTF-8 text entry from the already opened reference ZIP without extracting it.
    .PARAMETER Archive
    Open source-baseline archive.
    .PARAMETER EntryName
    Exact forward-slash archive entry name.
    #>
    param($Archive, [string] $EntryName)
    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) { throw "Source archive entry missing: $EntryName" }
    $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
    try { return $reader.ReadToEnd() }
    finally { $reader.Dispose() }
}

function Get-ArchiveHash {
    <#
    .SYNOPSIS
    Computes the SHA-256 of an archive entry for comparison with a visible package copy.
    .PARAMETER Archive
    Open source-baseline archive.
    .PARAMETER EntryName
    Exact source archive entry name.
    #>
    param($Archive, [string] $EntryName)
    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) { throw "Source archive entry missing: $EntryName" }
    $stream = $entry.Open()
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '') }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.formatVersion -ne 1) { throw 'Unsupported manifest format.' }
$paths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($file in $manifest.files) {
    if (-not $paths.Add($file.path)) { throw "Duplicate manifest entry: $($file.path)" }
    $absolutePath = Resolve-PackagePath $file.path
    if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) { throw "Missing file: $($file.path)" }
    if ((Get-Item -LiteralPath $absolutePath).Length -ne $file.bytes) { throw "Length mismatch: $($file.path)" }
    if ((Get-FileHash -LiteralPath $absolutePath -Algorithm SHA256).Hash -cne $file.sha256) {
        throw "SHA-256 mismatch: $($file.path)"
    }
}
foreach ($file in Get-ChildItem -LiteralPath $packageRoot -File -Recurse -Force) {
    $relative = $file.FullName.Substring($packagePrefix.Length).Replace('\', '/')
    if ($relative -eq 'MANIFEST.json') { continue }
    if (-not $paths.Contains($relative)) { throw "Unmanifested file: $relative" }
}

$baseline = Get-Content -LiteralPath (Join-Path $packageRoot 'reference/BASELINE.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($baseline.sourceCommit -cne $manifest.sourceCommit) { throw 'Manifest and baseline commit disagree.' }
$index = Get-Content -LiteralPath (Join-Path $packageRoot 'reference/acceptance-test-index.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($index.Count -ne 33 -or $baseline.regressionChecks -ne 33) { throw 'The 33-check baseline index is incomplete.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-PackagePath $baseline.sourceArchive))
try {
    foreach ($required in @('NetworkDriveRemapper.sln', 'global.json', 'Directory.Build.props', 'scripts/build.ps1',
        'src/NetworkDriveRemapper/NetworkDriveRemapper.csproj', 'src/NetworkDriveRemapper.Core/NetworkDriveRemapper.Core.csproj',
        'tests/NetworkDriveRemapper.Tests/NetworkDriveRemapper.Tests.csproj')) {
        if ($null -eq $archive.GetEntry($required)) { throw "Missing build input in archive: $required" }
    }
    if (@($archive.Entries | Where-Object { $_.FullName -match '^(\.git/|AIBuildPlan/|artifacts/)' }).Count -ne 0) {
        throw 'Source archive contains history, recursive package content, or generated artifacts.'
    }
    $comparisons = @{
        'assets/icon_edit2.png' = 'icon_edit2.png'
        'assets/icon_open.png' = 'icon_open.png'
        'assets/icon_remove.png' = 'icon_remove.png'
        'assets/icon_refresh.png' = 'icon_refresh.png'
        'assets/app-icon/NetworkDriveRemapper.ico' = 'assets/app-icon/NetworkDriveRemapper.ico'
        'assets/app-icon/NetworkDriveRemapper.png' = 'assets/app-icon/NetworkDriveRemapper.png'
        'reference/tests/NetworkDriveRemapper.Tests/Program.cs' = 'tests/NetworkDriveRemapper.Tests/Program.cs'
        'reference/tests/NetworkDriveRemapper.Tests/FakeNetworkDriveSystem.cs' = 'tests/NetworkDriveRemapper.Tests/FakeNetworkDriveSystem.cs'
        'reference/tests/NetworkDriveRemapper.Tests/NetworkDriveRemapper.Tests.csproj' = 'tests/NetworkDriveRemapper.Tests/NetworkDriveRemapper.Tests.csproj'
    }
    foreach ($copy in $comparisons.Keys) {
        if ((Get-ArchiveHash $archive $comparisons[$copy]) -cne (Get-FileHash -LiteralPath (Resolve-PackagePath $copy) -Algorithm SHA256).Hash) {
            throw "Package copy differs from the baseline archive: $copy"
        }
    }
    $testSource = Read-ArchiveText $archive 'tests/NetworkDriveRemapper.Tests/Program.cs'
    foreach ($check in $index) {
        if (-not $testSource.Contains('("' + $check.name + '", ' + $check.entry + ')')) { throw "Test index differs: $($check.name)" }
    }
    $helpSource = Read-ArchiveText $archive 'src/NetworkDriveRemapper/CredentialsHelpDialog.cs'
    $plainStrings = [System.IO.File]::ReadAllText((Join-Path $packageRoot 'STRINGS.md')).Replace('**', '')
    $paragraphs = [regex]::Matches($helpSource, 'AddParagraph\(paragraphs,\s*"[^"]+",\s*"([^"]+)"')
    if ($paragraphs.Count -ne 5) { throw 'Could not identify all five exact help paragraphs.' }
    foreach ($paragraph in $paragraphs) {
        if (-not $plainStrings.Contains($paragraph.Groups[1].Value)) { throw 'A help paragraph differs from its exact baseline wording.' }
    }
}
finally { $archive.Dispose() }

$linkCount = 0
foreach ($document in Get-ChildItem -LiteralPath $packageRoot -Filter '*.md' -File -Recurse) {
    $markdown = [System.IO.File]::ReadAllText($document.FullName)
    foreach ($link in [regex]::Matches($markdown, '\[[^\]]+\]\(([^)]+)\)')) {
        $target = $link.Groups[1].Value.Split('#')[0]
        if (-not $target -or $target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:') { continue }
        $resolved = [System.IO.Path]::GetFullPath((Join-Path $document.DirectoryName $target))
        if (-not $resolved.StartsWith($packagePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Markdown link escapes package: $($document.Name) -> $target"
        }
        if (-not (Test-Path -LiteralPath $resolved)) { throw "Broken Markdown link: $($document.Name) -> $target" }
        $linkCount++
    }
}
Write-Output "PASS: $($manifest.files.Count) files match SHA-256/length manifest."
Write-Output "PASS: baseline build inputs, 33 checks, copied tests/assets, and five exact help paragraphs agree."
Write-Output "PASS: $linkCount local Markdown links resolve within the portable package."

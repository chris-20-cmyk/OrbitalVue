[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $PublishDirectory,
    [Parameter(Mandatory = $true)] [string] $PackageVersion,
    [Parameter(Mandatory = $true)] [ValidatePattern('^[0-9a-f]{40}$')] [string] $SourceCommit,
    [string] $ReleaseDirectory,
    [switch] $RequireDelta
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PackageVersion -notmatch '^(\d+\.\d+\.\d+)-(?:alpha|beta|rc)\.(\d+)$') {
    throw 'Use a numbered alpha, beta, or rc preview version.'
}
$expectedFileVersion = "$($Matches[1]).$($Matches[2])"
$expectedProductVersion = "$PackageVersion+$SourceCommit"
foreach ($name in @('OrbitalVue.exe', 'OrbitalVue.dll')) {
    $version = (Get-Item -LiteralPath (Join-Path $PublishDirectory $name)).VersionInfo
    if ($version.FileVersion -cne $expectedFileVersion -or $version.ProductVersion -cne $expectedProductVersion) {
        throw "$name has version $($version.ProductVersion) / $($version.FileVersion); expected $expectedProductVersion / $expectedFileVersion."
    }
}
Write-Host "PASS: published application is $PackageVersion from $SourceCommit."
if (-not $ReleaseDirectory) { return }

$packageId = 'Chris.OrbitalVue'
$fullName = "$packageId-$PackageVersion-full.nupkg"
$deltaName = "$packageId-$PackageVersion-delta.nupkg"
$expectedAssets = @{
    $fullName = 'Full'
    "$packageId-win-Setup.exe" = 'Installer'
    "$packageId-win-Portable.zip" = 'Portable'
}
if ($RequireDelta) { $expectedAssets[$deltaName] = 'Delta' }
$assetManifest = @(Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'assets.win.json') -Raw | ConvertFrom-Json)
foreach ($name in $expectedAssets.Keys) {
    $entry = @($assetManifest | Where-Object { $_.RelativeFileName -ceq $name -and $_.Type -ceq $expectedAssets[$name] })
    if ($entry.Count -ne 1 -or (Get-Item -LiteralPath (Join-Path $ReleaseDirectory $name)).Length -le 0) {
        throw "Missing or incorrect release asset: $name."
    }
}

$feed = Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'releases.win.json') -Raw | ConvertFrom-Json
$currentFull = @($feed.Assets | Where-Object { $_.FileName -ceq $fullName -and $_.Type -ceq 'Full' -and $_.Version -ceq $PackageVersion })
if ($currentFull.Count -ne 1) { throw 'The update feed must contain the requested full package version.' }
if ($RequireDelta) {
    $currentDelta = @($feed.Assets | Where-Object { $_.FileName -ceq $deltaName -and $_.Type -ceq 'Delta' -and $_.Version -ceq $PackageVersion })
    if ($currentDelta.Count -ne 1) { throw 'The update feed must contain the requested delta package version.' }
}
foreach ($entry in $feed.Assets) {
    if ([IO.Path]::GetFileName($entry.FileName) -cne $entry.FileName -or $entry.PackageId -cne $packageId) {
        throw 'The update feed has an unexpected package identity or path.'
    }
    $path = Join-Path $ReleaseDirectory $entry.FileName
    if ((Get-Item -LiteralPath $path).Length -ne $entry.Size -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256 -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA1).Hash -ne $entry.SHA1) {
        throw "The update feed checksum or size is wrong for $($entry.FileName)."
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $ReleaseDirectory $fullName))
try {
    $reader = [IO.StreamReader]::new($archive.GetEntry("$packageId.nuspec").Open())
    try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($manifest.package.metadata.id -cne $packageId -or $manifest.package.metadata.version -cne $PackageVersion) {
        throw 'The full package metadata does not match the requested identity and version.'
    }
    foreach ($name in @('OrbitalVue.exe', 'OrbitalVue.dll', 'OrbitalVue.runtimeconfig.json')) {
        $stream = $archive.GetEntry("lib/app/$name").Open()
        try { $hash = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash } finally { $stream.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $PublishDirectory $name) -Algorithm SHA256).Hash) {
            throw "The full package contains a different $name than the verified publish output."
        }
    }
} finally { $archive.Dispose() }
Write-Host "PASS: $PackageVersion release assets, package contents, and update feed checksums agree."

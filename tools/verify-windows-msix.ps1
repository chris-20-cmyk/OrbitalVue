[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $PackagePath,
    [Parameter(Mandatory = $true)] [string] $ExpectedIdentityName,
    [Parameter(Mandatory = $true)] [string] $ExpectedPublisher,
    [Parameter(Mandatory = $true)] [string] $ExpectedPublisherDisplayName,
    [Parameter(Mandatory = $true)] [string] $ExpectedVersion,
    [Parameter(Mandatory = $true)] [string] $ExpectedPremiumProductId,
    [switch] $RequireUnsigned
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-MakeAppxPath {
    $kitBin = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits\10\bin'
    $candidate = Get-ChildItem -LiteralPath $kitBin -Filter makeappx.exe -File -Recurse |
        Where-Object { $_.FullName -match '[\\/]x64[\\/]makeappx\.exe$' } |
        Sort-Object { $_.VersionInfo.FileVersionRaw } -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) { throw 'MakeAppx.exe was not found.' }
    return $candidate.FullName
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\windows-msix'))
$resolvedPackage = [IO.Path]::GetFullPath($PackagePath)
if (-not $resolvedPackage.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The package must be inside artifacts\windows-msix.'
}
if (-not (Test-Path -LiteralPath $resolvedPackage -PathType Leaf)) { throw "Package not found: $resolvedPackage" }

$unpackDirectory = Join-Path $artifactRoot 'verify-unpacked'
if (-not ([IO.Path]::GetFullPath($unpackDirectory)).StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to clear a verification path outside artifacts\windows-msix.'
}
if (Test-Path -LiteralPath $unpackDirectory) { Remove-Item -LiteralPath $unpackDirectory -Recurse -Force }

$makeAppx = Get-MakeAppxPath
$makeAppxLog = @(& $makeAppx unpack /o /p $resolvedPackage /d $unpackDirectory 2>&1)
$makeAppxExitCode = $LASTEXITCODE
$makeAppxLog | Select-Object -Last 8 | ForEach-Object { Write-Host $_ }
if ($makeAppxExitCode -ne 0) { throw "MakeAppx unpack failed with exit code $makeAppxExitCode." }

$manifestPath = Join-Path $unpackDirectory 'AppxManifest.xml'
[xml] $manifest = [IO.File]::ReadAllText($manifestPath)
$namespaceManager = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespaceManager.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$namespaceManager.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
$namespaceManager.AddNamespace('uap10', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/10')
$namespaceManager.AddNamespace('rescap', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities')

$identity = $manifest.SelectSingleNode('/f:Package/f:Identity', $namespaceManager)
$properties = $manifest.SelectSingleNode('/f:Package/f:Properties', $namespaceManager)
$family = $manifest.SelectSingleNode('/f:Package/f:Dependencies/f:TargetDeviceFamily', $namespaceManager)
$application = $manifest.SelectSingleNode('/f:Package/f:Applications/f:Application', $namespaceManager)
$visuals = $manifest.SelectSingleNode('/f:Package/f:Applications/f:Application/uap:VisualElements', $namespaceManager)
$runFullTrust = $manifest.SelectSingleNode('/f:Package/f:Capabilities/rescap:Capability[@Name="runFullTrust"]', $namespaceManager)
if ($null -eq $identity -or $null -eq $properties -or $null -eq $family -or $null -eq $application -or
    $null -eq $visuals -or $null -eq $runFullTrust) {
    throw 'The MSIX manifest is missing a required desktop-package element.'
}
if ($identity.GetAttribute('Name') -ne $ExpectedIdentityName -or
    $identity.GetAttribute('Publisher') -ne $ExpectedPublisher -or
    $identity.GetAttribute('Version') -ne $ExpectedVersion -or
    $identity.GetAttribute('ProcessorArchitecture') -ne 'x64') {
    throw 'The MSIX identity does not exactly match the requested Partner Center values.'
}
if ($properties.PublisherDisplayName -ne $ExpectedPublisherDisplayName -or
    $family.GetAttribute('Name') -ne 'Windows.Desktop' -or
    $family.GetAttribute('MinVersion') -ne '10.0.19041.0') {
    throw 'The MSIX publisher or Windows.Desktop target is invalid.'
}
$uap10Namespace = 'http://schemas.microsoft.com/appx/manifest/uap/windows10/10'
if ($application.GetAttribute('Executable') -ne 'StreamVue.exe' -or
    $application.GetAttribute('RuntimeBehavior', $uap10Namespace) -ne 'packagedClassicApp' -or
    $application.GetAttribute('TrustLevel', $uap10Namespace) -ne 'mediumIL') {
    throw 'The MSIX application is not a medium-integrity packaged classic desktop app.'
}

foreach ($requiredFile in @('StreamVue.exe', 'StreamVue.dll', 'StreamVue.StoreBuild.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $unpackDirectory $requiredFile) -PathType Leaf)) {
        throw "The MSIX package is missing $requiredFile."
    }
}
$assemblyVersion = (Get-Item -LiteralPath (Join-Path $unpackDirectory 'StreamVue.dll')).VersionInfo
if ($assemblyVersion.FileVersion -ne $ExpectedVersion -or
    ($assemblyVersion.ProductVersion -ne $ExpectedVersion -and
     -not $assemblyVersion.ProductVersion.StartsWith($ExpectedVersion + '+', [StringComparison]::Ordinal))) {
    throw "The packaged app version does not match MSIX version $ExpectedVersion."
}
if (Get-ChildItem -LiteralPath $unpackDirectory -File -Recurse | Where-Object { $_.Name -match '^Velopack(?:\.|$)' }) {
    throw 'A Microsoft Store package must not contain Velopack runtime files.'
}

$audit = Get-Content -LiteralPath (Join-Path $unpackDirectory 'StreamVue.StoreBuild.json') -Raw | ConvertFrom-Json
$auditKeys = @($audit.PSObject.Properties.Name | Sort-Object)
$expectedAuditKeys = @('distribution', 'packageIdentityName', 'packageVersion', 'premiumProductId', 'schemaVersion', 'updater')
if (Compare-Object $expectedAuditKeys $auditKeys) { throw 'The Store build audit contains unexpected fields.' }
if ($audit.schemaVersion -ne 1 -or $audit.distribution -ne 'microsoft-store' -or
    $audit.packageIdentityName -ne $ExpectedIdentityName -or $audit.packageVersion -ne $ExpectedVersion -or
    $audit.premiumProductId -ne $ExpectedPremiumProductId -or $audit.updater -ne 'microsoft-store') {
    throw 'The Store build audit does not match the requested package configuration.'
}

Add-Type -AssemblyName System.Drawing
foreach ($asset in @(
    @{ Name = 'StoreLogo.png'; Size = 50 },
    @{ Name = 'Square44x44Logo.png'; Size = 44 },
    @{ Name = 'Square150x150Logo.png'; Size = 150 }
)) {
    $image = [Drawing.Image]::FromFile((Join-Path $unpackDirectory "Assets\$($asset.Name)"))
    try {
        if ($image.Width -ne $asset.Size -or $image.Height -ne $asset.Size) {
            throw "$($asset.Name) must be exactly $($asset.Size)x$($asset.Size) pixels."
        }
    }
    finally { $image.Dispose() }
}

if ($RequireUnsigned) {
    $signature = Get-AuthenticodeSignature -LiteralPath $resolvedPackage
    if ($signature.Status.ToString() -ne 'NotSigned') {
        throw "Expected an unsigned Partner Center submission package, but signature status is $($signature.Status)."
    }
}

Write-Host "MSIX contract verified: $ExpectedIdentityName $ExpectedVersion, Microsoft Store managed, premium product $ExpectedPremiumProductId."

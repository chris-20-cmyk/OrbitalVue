[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $IdentityName,

    [Parameter(Mandatory = $true)]
    [string] $Publisher,

    [Parameter(Mandatory = $true)]
    [string] $PublisherDisplayName,

    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $PremiumProductId,

    [switch] $SkipRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Matches([string] $Value, [string] $Pattern, [string] $Name) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -notmatch $Pattern) {
        throw "$Name is invalid. Copy the exact value from Microsoft Partner Center."
    }
}

function Get-MakeAppxPath {
    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $kitBin = Join-Path $programFilesX86 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $kitBin -PathType Container)) {
        throw 'MakeAppx.exe was not found. Install the Windows 10/11 SDK packaging tools.'
    }

    $candidate = Get-ChildItem -LiteralPath $kitBin -Filter makeappx.exe -File -Recurse |
        Where-Object { $_.FullName -match '[\\/]x64[\\/]makeappx\.exe$' } |
        Sort-Object { $_.VersionInfo.FileVersionRaw } -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw 'MakeAppx.exe was not found. Install the Windows 10/11 SDK packaging tools.'
    }
    return $candidate.FullName
}

function Write-SquarePng([string] $SourcePath, [string] $DestinationPath, [int] $Size) {
    Add-Type -AssemblyName System.Drawing
    $source = [System.Drawing.Image]::FromFile($SourcePath)
    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($source, 0, 0, $Size, $Size)
        $bitmap.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
        $source.Dispose()
    }
}

Assert-Matches $IdentityName '^[A-Za-z0-9.-]{3,50}$' 'IdentityName'
Assert-Matches $PremiumProductId '^[A-Za-z0-9._-]{3,256}$' 'PremiumProductId'
if (-not $Publisher.StartsWith('CN=', [StringComparison]::OrdinalIgnoreCase) -or $Publisher.Length -gt 8192 -or $Publisher.IndexOfAny([char[]]"`r`n") -ge 0) {
    throw 'Publisher is invalid. Copy the complete CN= value from Microsoft Partner Center.'
}
if ([string]::IsNullOrWhiteSpace($PublisherDisplayName) -or $PublisherDisplayName.Length -gt 256 -or
    $PublisherDisplayName.IndexOfAny([char[]]"`r`n") -ge 0) {
    throw 'PublisherDisplayName is invalid. Copy the exact value from Microsoft Partner Center.'
}

$versionParts = $Version.Split('.')
if ($versionParts.Count -ne 4 -or @($versionParts | Where-Object { $_ -notmatch '^(0|[1-9]\d{0,4})$' -or [int]$_ -gt 65535 }).Count -gt 0) {
    throw 'Version must contain four numeric parts from 0 through 65535.'
}
if ([int]$versionParts[3] -ne 0) {
    throw 'The fourth MSIX version part must be 0 because Microsoft Store reserves it.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\windows-msix'))
$publishDirectory = Join-Path $artifactRoot 'publish'
$stageDirectory = Join-Path $artifactRoot 'stage'
$assetDirectory = Join-Path $stageDirectory 'Assets'
$projectPath = Join-Path $repositoryRoot 'src\OrbitalVue.Player\OrbitalVue.Player.csproj'
$templatePath = Join-Path $repositoryRoot 'packaging\windows-msix\AppxManifest.template.xml'
$sourceIcon = Join-Path $repositoryRoot 'src\OrbitalVue.Player\Assets\orbitalvue-256.png'

foreach ($path in @($publishDirectory, $stageDirectory)) {
    $fullPath = [IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear a path outside the Windows MSIX artifact directory: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $publishDirectory, $assetDirectory -Force | Out-Null

$localDotnet = Join-Path $repositoryRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet -PathType Leaf) {
    $localDotnet
}
else {
    (Get-Command dotnet -ErrorAction Stop).Source
}
$publishArguments = @(
    'publish', $projectPath,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-o', $publishDirectory,
    '-p:PublishSingleFile=false',
    '-p:OrbitalVueDistributionMode=Store',
    "-p:OrbitalVuePremiumProductId=$PremiumProductId",
    "-p:Version=$Version",
    "-p:FileVersion=$Version",
    "-p:AssemblyVersion=$Version"
)
if ($SkipRestore) { $publishArguments += '--no-restore' }
& $dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $stageDirectory -Recurse -Force
$unusedX86Vlc = Join-Path $stageDirectory 'libvlc\win-x86'
if (Test-Path -LiteralPath $unusedX86Vlc -PathType Container) {
    $resolvedUnusedX86Vlc = [IO.Path]::GetFullPath($unusedX86Vlc)
    if (-not $resolvedUnusedX86Vlc.StartsWith([IO.Path]::GetFullPath($stageDirectory) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove an architecture directory outside the MSIX staging directory.'
    }
    Remove-Item -LiteralPath $resolvedUnusedX86Vlc -Recurse -Force
}
Get-ChildItem -LiteralPath $stageDirectory -Filter '*.pdb' -File -Recurse | Remove-Item -Force

$escape = { param([string] $Value) [Security.SecurityElement]::Escape($Value) }
$manifest = [IO.File]::ReadAllText($templatePath)
$manifest = $manifest.Replace('@@IDENTITY_NAME@@', (& $escape $IdentityName))
$manifest = $manifest.Replace('@@PUBLISHER@@', (& $escape $Publisher))
$manifest = $manifest.Replace('@@PUBLISHER_DISPLAY_NAME@@', (& $escape $PublisherDisplayName))
$manifest = $manifest.Replace('@@VERSION@@', (& $escape $Version))
[IO.File]::WriteAllText(
    (Join-Path $stageDirectory 'AppxManifest.xml'),
    $manifest,
    [Text.UTF8Encoding]::new($false))

Write-SquarePng $sourceIcon (Join-Path $assetDirectory 'StoreLogo.png') 50
Write-SquarePng $sourceIcon (Join-Path $assetDirectory 'Square44x44Logo.png') 44
Write-SquarePng $sourceIcon (Join-Path $assetDirectory 'Square150x150Logo.png') 150

$buildAudit = [ordered]@{
    schemaVersion = 1
    distribution = 'microsoft-store'
    packageIdentityName = $IdentityName
    packageVersion = $Version
    premiumProductId = $PremiumProductId
    updater = 'microsoft-store'
}
[IO.File]::WriteAllText(
    (Join-Path $stageDirectory 'OrbitalVue.StoreBuild.json'),
    ($buildAudit | ConvertTo-Json -Depth 3),
    [Text.UTF8Encoding]::new($false))

$packageName = "OrbitalVue-$Version-win-x64-microsoft-store-unsigned.msix"
$packagePath = Join-Path $artifactRoot $packageName
if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
$makeAppx = Get-MakeAppxPath
$makeAppxLog = @(& $makeAppx pack /o /d $stageDirectory /p $packagePath 2>&1)
$makeAppxExitCode = $LASTEXITCODE
$makeAppxLog | Select-Object -Last 12 | ForEach-Object { Write-Host $_ }
if ($makeAppxExitCode -ne 0) { throw "MakeAppx failed with exit code $makeAppxExitCode." }

& (Join-Path $repositoryRoot 'tools\verify-windows-msix.ps1') `
    -PackagePath $packagePath `
    -ExpectedIdentityName $IdentityName `
    -ExpectedPublisher $Publisher `
    -ExpectedPublisherDisplayName $PublisherDisplayName `
    -ExpectedVersion $Version `
    -ExpectedPremiumProductId $PremiumProductId `
    -RequireUnsigned

Write-Host "Built and verified Microsoft Store submission package: $packagePath"

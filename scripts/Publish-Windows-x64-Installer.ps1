param(
    [switch]$AllowDirtyWorkingTree,
    [switch]$SkipDesktopCopy
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$portablePublisher = Join-Path $PSScriptRoot "Publish-Windows-x64.ps1"
$installerDefinition = Join-Path $repo "installer\PC-SPA.iss"
$publishDirectory = Join-Path $repo "artifacts\publish\win-x64"
$installerDirectory = Join-Path $repo "artifacts\installer"
$installerName = "PC-SPA-1.0.0-win-x64-setup.exe"
$installerPath = Join-Path $installerDirectory $installerName
$hashPath = "$installerPath.sha256"
$desktopDirectory = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::DesktopDirectory)
$desktopInstallerPath = Join-Path $desktopDirectory $installerName
$desktopHashPath = Join-Path $desktopDirectory "$installerName.sha256"

Set-Location $repo

if (-not (Test-Path -LiteralPath $portablePublisher -PathType Leaf)) {
    throw "Verified portable publisher is missing: $portablePublisher"
}

if (-not (Test-Path -LiteralPath $installerDefinition -PathType Leaf)) {
    throw "Inno Setup definition is missing: $installerDefinition"
}

$compilerCandidates = @()

if (-not [string]::IsNullOrWhiteSpace($env:INNO_SETUP_COMPILER)) {
    $compilerCandidates += $env:INNO_SETUP_COMPILER
}

if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    $compilerCandidates += Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
}

if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
    $compilerCandidates += Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
}

if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
    $compilerCandidates += Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"
}

$compiler = $compilerCandidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($compiler)) {
    throw "Inno Setup 6 compiler was not found. Install Inno Setup 6 or set INNO_SETUP_COMPILER to ISCC.exe."
}

$portableArguments = @{}
$portableArguments.SkipDesktopCopy = $true
if ($AllowDirtyWorkingTree) {
    $portableArguments.AllowDirtyWorkingTree = $true
}

Write-Host ""
Write-Host "Verified portable publish..." -ForegroundColor Cyan
& $portablePublisher @portableArguments

if ($LASTEXITCODE -ne 0) {
    throw "Verified portable publish failed. Do not create an installer."
}

if (-not (Test-Path -LiteralPath $publishDirectory -PathType Container)) {
    throw "Portable publish directory is missing: $publishDirectory"
}

Remove-Item -LiteralPath $installerDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

Write-Host ""
Write-Host "Windows x64 installer compilation..." -ForegroundColor Cyan
& $compiler "/DSourceRoot=$publishDirectory" $installerDefinition

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed."
}

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Expected Windows installer was not created: $installerPath"
}

$installerVersion = (Get-Item -LiteralPath $installerPath).VersionInfo
if (-not $installerVersion.ProductVersion.StartsWith("1.0.0")) {
    throw "Installer ProductVersion is '$($installerVersion.ProductVersion)', expected 1.0.0."
}

$installerHash = (
    Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
).Hash.ToLowerInvariant()

"$installerHash  $installerName" |
    Set-Content -LiteralPath $hashPath -Encoding ASCII

if (-not $SkipDesktopCopy) {
    if ([string]::IsNullOrWhiteSpace($desktopDirectory) -or
        -not (Test-Path -LiteralPath $desktopDirectory -PathType Container)) {
        throw "Windows Desktop folder could not be resolved: $desktopDirectory"
    }

    Copy-Item -LiteralPath $installerPath -Destination $desktopInstallerPath -Force
    Copy-Item -LiteralPath $hashPath -Destination $desktopHashPath -Force
}

$installerSignature = Get-AuthenticodeSignature -LiteralPath $installerPath
$publishedPeFiles = @(
    Get-ChildItem -LiteralPath $publishDirectory -File -Recurse |
        Where-Object { $_.Extension -in @(".exe", ".dll") }
)
$unsignedPeFiles = @(
    $publishedPeFiles |
        Where-Object {
            (Get-AuthenticodeSignature -LiteralPath $_.FullName).Status -ne "Valid"
        }
)

Write-Host ""
Write-Host "Windows x64 installer created successfully." -ForegroundColor Green
Write-Host "Installer: $installerPath" -ForegroundColor Green
Write-Host "SHA-256: $installerHash" -ForegroundColor Green
Write-Host "Hash file: $hashPath" -ForegroundColor Green
if (-not $SkipDesktopCopy) {
    Write-Host "Desktop installer: $desktopInstallerPath" -ForegroundColor Green
    Write-Host "Desktop hash: $desktopHashPath" -ForegroundColor Green
}
Write-Host "Installer signature: $($installerSignature.Status)" -ForegroundColor Yellow
Write-Host "Published PE files without a valid signature: $($unsignedPeFiles.Count)" -ForegroundColor Yellow
Write-Host "No automatic restart is configured." -ForegroundColor Green

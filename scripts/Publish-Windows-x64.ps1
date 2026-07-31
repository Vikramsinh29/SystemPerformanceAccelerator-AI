param(
    [switch]$AllowDirtyWorkingTree
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repo "SystemPerformanceAccelerator.slnx"
$desktopProject = Join-Path $repo "src\SystemPerformanceAccelerator.Desktop\SystemPerformanceAccelerator.Desktop.csproj"

$version = "1.0.0"
$runtimeIdentifier = "win-x64"
$releaseName = "PC-SPA-$version-$runtimeIdentifier-portable"

$publishDirectory = Join-Path $repo "artifacts\publish\$runtimeIdentifier"
$releaseRoot = Join-Path $repo "artifacts\releases"
$stagingRoot = Join-Path $releaseRoot "_staging"
$releaseFolder = Join-Path $stagingRoot $releaseName
$zipPath = Join-Path $releaseRoot "$releaseName.zip"
$hashPath = "$zipPath.sha256"
$desktopDirectory = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::DesktopDirectory)
$desktopZipPath = Join-Path $desktopDirectory (Split-Path -Leaf $zipPath)
$desktopHashPath = Join-Path $desktopDirectory (Split-Path -Leaf $hashPath)

Set-Location $repo

if (-not $AllowDirtyWorkingTree) {
    $workingTreeChanges = @(git status --porcelain=v1 -uall)
    if ($workingTreeChanges.Count -gt 0) {
        git status --short
        throw "A release must be produced from a clean working tree."
    }
}

$branch = (git branch --show-current).Trim()
$commit = (git rev-parse HEAD).Trim()

if ([string]::IsNullOrWhiteSpace($branch)) {
    throw "Unable to determine the current Git branch."
}

if ([string]::IsNullOrWhiteSpace($commit)) {
    throw "Unable to determine the current Git commit."
}

Get-Process -Name "PC-SPA", "SystemPerformanceAccelerator.Desktop" -ErrorAction SilentlyContinue |
    Stop-Process -Force

Remove-Item -LiteralPath $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $hashPath -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releaseFolder -Force | Out-Null

Write-Host ""
Write-Host "Clean Release build..." -ForegroundColor Cyan

dotnet clean $solution -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Release clean failed."
}

dotnet build $solution -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Release build failed. Do not package a stale executable."
}

Write-Host ""
Write-Host "Full test suite..." -ForegroundColor Cyan

dotnet test $solution -c Release --no-build
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed. Do not create a release package."
}

Write-Host ""
Write-Host "Self-contained Windows x64 publish..." -ForegroundColor Cyan

dotnet publish $desktopProject `
    -c Release `
    -r $runtimeIdentifier `
    --self-contained true `
    -p:PublishProfile=Windows-x64-Portable `
    -o $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "Windows x64 publish failed."
}

$executable = Join-Path $publishDirectory "PC-SPA.exe"
$runtimeConfig = Join-Path $publishDirectory "PC-SPA.runtimeconfig.json"
$dependencyManifest = Join-Path $publishDirectory "PC-SPA.deps.json"
$coreRuntime = Join-Path $publishDirectory "coreclr.dll"

foreach ($requiredFile in @(
    $executable,
    $runtimeConfig,
    $dependencyManifest,
    $coreRuntime
)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required self-contained publish file is missing: $requiredFile"
    }
}

$pdbFiles = @(
    Get-ChildItem -LiteralPath $publishDirectory -Filter "*.pdb" -File -Recurse
)

if ($pdbFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "Removing publish-time debugging symbol files..." -ForegroundColor Cyan

    foreach ($pdbFile in $pdbFiles) {
        Write-Host "Removing: $($pdbFile.FullName)"
        Remove-Item -LiteralPath $pdbFile.FullName -Force
    }
}

$remainingPdbFiles = @(
    Get-ChildItem -LiteralPath $publishDirectory -Filter "*.pdb" -File -Recurse
)

if ($remainingPdbFiles.Count -gt 0) {
    $remainingPdbFiles | ForEach-Object { Write-Host $_.FullName }
    throw "Debugging symbol files remain after release-output cleanup."
}

$versionInfo = (Get-Item -LiteralPath $executable).VersionInfo

if (-not $versionInfo.FileVersion.StartsWith($version)) {
    throw "Published executable FileVersion is '$($versionInfo.FileVersion)', expected $version."
}

if (-not $versionInfo.ProductVersion.StartsWith($version)) {
    throw "Published executable ProductVersion is '$($versionInfo.ProductVersion)', expected $version."
}

Copy-Item `
    -Path (Join-Path $publishDirectory "*") `
    -Destination $releaseFolder `
    -Recurse `
    -Force

$releaseNotes = @"
PC-SPA $version

Package:
- Windows 10/11 x64
- Self-contained .NET desktop application
- Portable ZIP; no installer
- Requests administrator permission at launch for protected startup controls
- No code signing
- No cloud service or telemetry

Launch:
1. Extract the complete ZIP.
2. Keep all extracted files together.
3. Run PC-SPA.exe.

Windows may display an Unknown Publisher or SmartScreen warning because this
release is intentionally unsigned.

Source commit:
$commit

Source branch:
$branch
"@

$releaseNotesPath = Join-Path $releaseFolder "RELEASE-NOTES.txt"
Set-Content `
    -LiteralPath $releaseNotesPath `
    -Value $releaseNotes `
    -Encoding UTF8

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

Compress-Archive `
    -Path (Join-Path $stagingRoot "*") `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal `
    -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "Portable release ZIP was not created."
}

$zipHash = (
    Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
).Hash.ToLowerInvariant()

"$zipHash  $(Split-Path -Leaf $zipPath)" |
    Set-Content -LiteralPath $hashPath -Encoding ASCII

$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entryNames = @(
        $archive.Entries |
            ForEach-Object { $_.FullName.Replace('\', '/') }
    )

    $requiredArchiveEntries = @(
        "$releaseName/PC-SPA.exe",
        "$releaseName/PC-SPA.runtimeconfig.json",
        "$releaseName/PC-SPA.deps.json",
        "$releaseName/coreclr.dll",
        "$releaseName/RELEASE-NOTES.txt"
    )

    foreach ($requiredEntry in $requiredArchiveEntries) {
        if ($entryNames -notcontains $requiredEntry) {
            throw "Required file is missing from the portable ZIP: $requiredEntry"
        }
    }
}
finally {
    $archive.Dispose()
}

Remove-Item -LiteralPath $stagingRoot -Recurse -Force

if ([string]::IsNullOrWhiteSpace($desktopDirectory) -or
    -not (Test-Path -LiteralPath $desktopDirectory -PathType Container)) {
    throw "Windows Desktop folder could not be resolved: $desktopDirectory"
}

Copy-Item -LiteralPath $zipPath -Destination $desktopZipPath -Force
Copy-Item -LiteralPath $hashPath -Destination $desktopHashPath -Force

foreach ($desktopCopy in @($desktopZipPath, $desktopHashPath)) {
    if (-not (Test-Path -LiteralPath $desktopCopy -PathType Leaf)) {
        throw "Desktop release copy was not created: $desktopCopy"
    }
}

$signature = Get-AuthenticodeSignature -LiteralPath $executable

Write-Host ""
Write-Host "Windows x64 portable release created successfully." -ForegroundColor Green
Write-Host "Version: $version" -ForegroundColor Green
Write-Host "Commit: $commit" -ForegroundColor Green
Write-Host "Executable signature: $($signature.Status)" -ForegroundColor Yellow
Write-Host "ZIP: $zipPath" -ForegroundColor Green
Write-Host "SHA-256: $zipHash" -ForegroundColor Green
Write-Host "Hash file: $hashPath" -ForegroundColor Green
Write-Host "Desktop ZIP: $desktopZipPath" -ForegroundColor Green
Write-Host "Desktop hash: $desktopHashPath" -ForegroundColor Green
Write-Host ""
Write-Host "Full automated test suite passed before packaging." -ForegroundColor Cyan

param(
    [switch]$AllowDirtyWorkingTree
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$installerPublisher = Join-Path $PSScriptRoot "Publish-Windows-x64-Installer.ps1"
$version = "1.0.0"
$bundleName = "PC-SPA-$version-win-x64-controlled-beta"
$installerName = "PC-SPA-$version-win-x64-setup.exe"
$installerDirectory = Join-Path $repo "artifacts\installer"
$installerPath = Join-Path $installerDirectory $installerName
$installerHashPath = "$installerPath.sha256"
$releaseDirectory = Join-Path $repo "artifacts\beta"
$stagingDirectory = Join-Path $releaseDirectory "_staging\$bundleName"
$bundlePath = Join-Path $releaseDirectory "$bundleName.zip"
$bundleHashPath = "$bundlePath.sha256"
$desktopDirectory = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::DesktopDirectory)
$desktopBundlePath = Join-Path $desktopDirectory (Split-Path -Leaf $bundlePath)
$desktopBundleHashPath = Join-Path $desktopDirectory (Split-Path -Leaf $bundleHashPath)

Set-Location $repo

if (-not (Test-Path -LiteralPath $installerPublisher -PathType Leaf)) {
    throw "Verified installer publisher is missing: $installerPublisher"
}

$installerArguments = @{}
if ($AllowDirtyWorkingTree) {
    $installerArguments.AllowDirtyWorkingTree = $true
}

Write-Host ""
Write-Host "Verified Windows installer publish..." -ForegroundColor Cyan
& $installerPublisher @installerArguments

if ($LASTEXITCODE -ne 0) {
    throw "Verified installer publish failed. Do not create a beta bundle."
}

foreach ($requiredFile in @($installerPath, $installerHashPath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required controlled-beta file is missing: $requiredFile"
    }
}

$commit = (git rev-parse HEAD).Trim()
$branch = (git branch --show-current).Trim()

if ([string]::IsNullOrWhiteSpace($commit)) {
    throw "Unable to determine the source commit."
}

if ([string]::IsNullOrWhiteSpace($branch)) {
    throw "Unable to determine the source branch."
}

$installerHash = (
    Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
).Hash.ToLowerInvariant()
$recordedInstallerHash = @(
    (Get-Content -LiteralPath $installerHashPath -Raw) -split '\s+'
)[0].ToLowerInvariant()

if ($installerHash -ne $recordedInstallerHash) {
    throw "Installer SHA-256 does not match its recorded hash file."
}

Remove-Item -LiteralPath $releaseDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

Copy-Item -LiteralPath $installerPath -Destination $stagingDirectory -Force
Copy-Item -LiteralPath $installerHashPath -Destination $stagingDirectory -Force

$betaReadme = @"
PC-SPA $version - CONTROLLED BETA

This package is for invited beta testers only. It is not a publicly trusted or
Microsoft Store release.

SYSTEM REQUIREMENTS
- Windows 10/11 x64
- Administrator permission is required for installation and protected tools
- Keep the installer and SHA-256 file together

SECURITY AND PRIVACY
- The installer is currently unsigned. Windows may show Unknown Publisher or
  a Microsoft Defender SmartScreen warning.
- Verify the installer SHA-256 before running it.
- PC-SPA core tools work locally without a cloud account or telemetry.
- Beta Error Feedback is the only optional network feature. It sends only
  after the tester reviews the report, selects consent, and presses the send
  action. Personal files and file contents are never attached or uploaded.
- Nothing is sent automatically. If the feedback service is unavailable,
  PC-SPA offers a reviewed local ZIP instead.
- PC-SPA never automatically restarts Windows.
- Review every cleanup selection and confirmation before continuing.

VERIFY IN POWERSHELL
`$expected = "$installerHash"
`$actual = (Get-FileHash ".\$installerName" -Algorithm SHA256).Hash.ToLowerInvariant()
`$actual -eq `$expected

The verification result must be True.

INSTALL
1. Verify the SHA-256.
2. Run $installerName.
3. Review the administrator and unsigned-publisher prompts.
4. Keep Create a desktop shortcut selected unless you do not want it.

UNINSTALL
- Use Windows Settings > Apps, then select PC-SPA and choose Uninstall.
- Uninstall does not restart Windows automatically.
- Local settings, diagnostics, and Windows Repair history are retained.

SOURCE
Branch: $branch
Commit: $commit

Do not redistribute this controlled-beta package publicly.
"@

$feedbackChecklist = @"
PC-SPA CONTROLLED-BETA FEEDBACK CHECKLIST

Please report only what you actually tested. Do not fabricate repair results,
force an unhealthy assessment, or delete personal files just for testing.

ENVIRONMENT
- Windows version and build:
- PC make/model:
- Display resolution and scaling:
- Restored or maximized window:

CHECKS
- Installer completed without requesting a restart:
- Desktop and Start Menu shortcuts worked:
- Cleaner opened and completed a read-only scan:
- Health Check opened and completed:
- Custom Clean preview opened:
- Auto Clean Schedule opened:
- Large File Finder opened:
- Duplicate Finder opened:
- Startup Manager opened:
- Windows Repair opened without forced repair activity:
- System Monitor displayed live values:
- Settings opened and saved normally:
- Beta Error Feedback preview clearly disclosed the optional HTTPS send:
- A successful send displayed a BETA reference that could be copied:
- With the network unavailable, local ZIP fallback was offered:
- Mouse-wheel and table scrolling worked:
- Restored and maximized layouts remained readable:
- Uninstall completed without requesting a restart:

PROBLEM REPORT
- Tool/page:
- Exact action:
- Expected result:
- Actual result:
- Exact error text:
- Reproducible every time: Yes / No

If diagnostics are enabled, review any diagnostic export before sharing it.
Beta Error Feedback sends only after explicit preview and consent. PC-SPA
never uploads diagnostic evidence automatically or attaches personal files.
"@

Set-Content `
    -LiteralPath (Join-Path $stagingDirectory "BETA-README.txt") `
    -Value $betaReadme `
    -Encoding UTF8

Set-Content `
    -LiteralPath (Join-Path $stagingDirectory "BETA-FEEDBACK-CHECKLIST.txt") `
    -Value $feedbackChecklist `
    -Encoding UTF8

Compress-Archive `
    -Path (Join-Path (Split-Path -Parent $stagingDirectory) "*") `
    -DestinationPath $bundlePath `
    -CompressionLevel Optimal `
    -Force

if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) {
    throw "Controlled-beta ZIP was not created."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($bundlePath)
try {
    $entryNames = @(
        $archive.Entries |
            ForEach-Object { $_.FullName.Replace('\', '/') }
    )

    foreach ($requiredEntry in @(
        "$bundleName/$installerName",
        "$bundleName/$installerName.sha256",
        "$bundleName/BETA-README.txt",
        "$bundleName/BETA-FEEDBACK-CHECKLIST.txt"
    )) {
        if ($entryNames -notcontains $requiredEntry) {
            throw "Controlled-beta ZIP is missing: $requiredEntry"
        }
    }
}
finally {
    $archive.Dispose()
}

$bundleHash = (
    Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256
).Hash.ToLowerInvariant()

"$bundleHash  $(Split-Path -Leaf $bundlePath)" |
    Set-Content -LiteralPath $bundleHashPath -Encoding ASCII

if ([string]::IsNullOrWhiteSpace($desktopDirectory) -or
    -not (Test-Path -LiteralPath $desktopDirectory -PathType Container)) {
    throw "Windows Desktop folder could not be resolved: $desktopDirectory"
}

Copy-Item -LiteralPath $bundlePath -Destination $desktopBundlePath -Force
Copy-Item -LiteralPath $bundleHashPath -Destination $desktopBundleHashPath -Force

foreach ($desktopFile in @($desktopBundlePath, $desktopBundleHashPath)) {
    if (-not (Test-Path -LiteralPath $desktopFile -PathType Leaf)) {
        throw "Desktop controlled-beta file was not created: $desktopFile"
    }
}

$installerSignature = Get-AuthenticodeSignature -LiteralPath $installerPath

Write-Host ""
Write-Host "Controlled-beta bundle created successfully." -ForegroundColor Green
Write-Host "Source commit: $commit" -ForegroundColor Green
Write-Host "Installer signature: $($installerSignature.Status)" -ForegroundColor Yellow
Write-Host "Installer SHA-256: $installerHash" -ForegroundColor Green
Write-Host "Bundle: $bundlePath" -ForegroundColor Green
Write-Host "Bundle SHA-256: $bundleHash" -ForegroundColor Green
Write-Host "Bundle hash file: $bundleHashPath" -ForegroundColor Green
Write-Host "Desktop bundle: $desktopBundlePath" -ForegroundColor Green
Write-Host "Desktop bundle hash: $desktopBundleHashPath" -ForegroundColor Green
Write-Host "Distribution scope: Invited beta testers only" -ForegroundColor Yellow

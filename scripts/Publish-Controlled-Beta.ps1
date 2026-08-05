param(
    [switch]$AllowDirtyWorkingTree
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$installerPublisher = Join-Path $PSScriptRoot "Publish-Windows-x64-Installer.ps1"
$version = "1.0.0-beta.1"
$bundleName = "PC-SPA-$version-win-x64-controlled-beta"
$installerName = "PC-SPA-$version-win-x64-setup.exe"
$bundleInstallerName = "2-INSTALL-PC-SPA.exe"
$installerDirectory = Join-Path $repo "artifacts\installer"
$installerPath = Join-Path $installerDirectory $installerName
$installerHashPath = "$installerPath.sha256"
$brandingDirectory = Join-Path $repo "src\SystemPerformanceAccelerator.Desktop\Assets\Branding"
$guideLogoPath = Join-Path $brandingDirectory "PC-SPA-Exact-Original-2048x2048.png"
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
$installerArguments.SkipDesktopCopy = $true
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

foreach ($brandingFile in @($guideLogoPath)) {
    if (-not (Test-Path -LiteralPath $brandingFile -PathType Leaf)) {
        throw "Required installation-guide branding asset is missing: $brandingFile"
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
$informationDirectory = Join-Path $stagingDirectory "Beta-Information"
New-Item -ItemType Directory -Path $informationDirectory -Force | Out-Null

Copy-Item `
    -LiteralPath $installerPath `
    -Destination (Join-Path $stagingDirectory $bundleInstallerName) `
    -Force

"$installerHash  $bundleInstallerName" |
    Set-Content `
        -LiteralPath (Join-Path $informationDirectory "$bundleInstallerName.sha256") `
        -Encoding ASCII

$guideLogoBase64 = [Convert]::ToBase64String(
    [IO.File]::ReadAllBytes($guideLogoPath))

$betaReadme = @"
PC-SPA $version - CONTROLLED BETA

This package is for invited beta testers only. It is not a publicly trusted or
Microsoft Store release.

SYSTEM REQUIREMENTS
- Windows 10/11 x64
- Administrator permission is required for installation and protected tools
- Keep the complete extracted folder together

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
`$actual = (Get-FileHash "..\$bundleInstallerName" -Algorithm SHA256).Hash.ToLowerInvariant()
`$actual -eq `$expected

The verification result must be True.

INSTALL
1. Verify the SHA-256.
2. Run $bundleInstallerName from the main extracted folder.
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

$installationGuide = @"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Install PC-SPA $version</title>
  <style>
    :root { color-scheme: dark; }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      background: #080b0f;
      color: #f5f5f5;
      font-family: "Segoe UI", Arial, sans-serif;
      font-size: 16px;
      line-height: 1.55;
    }
    main { width: min(820px, calc(100% - 32px)); margin: 40px auto; }
    .hero {
      margin-bottom: 26px;
      padding: 18px 20px 16px;
      background: #02090d;
      border: 1px solid #34302a;
      border-radius: 12px;
    }
    .logo-crop {
      position: relative;
      width: min(100%, 560px);
      aspect-ratio: 1.43 / 1;
      margin: 0 auto;
      overflow: hidden;
      background: #02090d;
    }
    .logo-lockup {
      position: absolute;
      inset: 0 auto auto 0;
      display: block;
      width: 100%;
      height: auto;
      transform: translateY(-15.5%);
    }
    .hero-copy {
      padding: 14px 12px 0;
      text-align: center;
    }
    h1 { margin: 0 0 6px; font-size: 28px; line-height: 1.18; }
    .subtitle { margin: 0; color: #c7cbd1; font-size: 14px; }
    .card {
      margin: 16px 0;
      padding: 24px;
      background: #171b21;
      border: 1px solid #3f3826;
      border-radius: 12px;
    }
    h2 { margin: 0 0 16px; font-size: 21px; }
    ol { margin: 0; padding-left: 24px; }
    li { margin: 10px 0; padding-left: 6px; }
    code {
      color: #ffd66d;
      font-family: Consolas, "Courier New", monospace;
      font-size: 0.95em;
    }
    .notice { border-color: #b88718; background: #2a2315; }
    .notice strong { color: #ffd66d; }
    .facts { display: grid; gap: 10px; }
    .fact::before { content: "\2713"; color: #4bc48a; margin-right: 10px; }
    .small { color: #aeb4bd; font-size: 14px; }
    @media (max-width: 680px) {
      main { margin: 24px auto; }
      .hero { padding: 14px 14px 15px; }
      .logo-crop { width: min(100%, 500px); aspect-ratio: 1.43 / 1; }
      .hero-copy { padding-top: 12px; }
      h1 { font-size: 25px; }
    }
  </style>
</head>
<body>
  <main>
    <div class="hero">
      <div class="logo-crop">
        <img class="logo-lockup"
             src="data:image/png;base64,$guideLogoBase64"
             alt="PC-SPA - System Performance Accelerator">
      </div>
      <div class="hero-copy">
        <h1>Install PC-SPA Controlled Beta</h1>
        <p class="subtitle">Version $version for invited Windows 10/11 x64 testers</p>
      </div>
    </div>

    <section class="card">
      <h2>Install in three steps</h2>
      <ol>
        <li>Make sure the downloaded ZIP has been fully extracted.</li>
        <li>In this folder, double-click <code>$bundleInstallerName</code>.</li>
        <li>Approve the Windows administrator prompt and follow the installer.</li>
      </ol>
    </section>

    <section class="card notice">
      <h2>Windows protection notice</h2>
      <p><strong>This controlled-beta installer is currently unsigned.</strong>
      Windows may show Unknown Publisher or a SmartScreen warning. If this
      package came directly from the PC-SPA beta programme, select
      <strong>More info</strong>, verify that the application is PC-SPA, and
      then select <strong>Run anyway</strong>.</p>
    </section>

    <section class="card">
      <h2>What PC-SPA will not do automatically</h2>
      <div class="facts">
        <div class="fact">It will not restart Windows automatically.</div>
        <div class="fact">It will not upload personal files or file contents.</div>
        <div class="fact">Beta feedback is sent only after review and consent.</div>
        <div class="fact">Uninstall retains local settings and history.</div>
      </div>
    </section>

    <p class="small">Optional security information and the tester feedback
    checklist are available in the <strong>Beta-Information</strong> folder.
    This page contains no scripts, tracking, or network content.</p>
  </main>
</body>
</html>
"@

Set-Content `
    -LiteralPath (Join-Path $informationDirectory "BETA-README.txt") `
    -Value $betaReadme `
    -Encoding UTF8

Set-Content `
    -LiteralPath (Join-Path $informationDirectory "BETA-FEEDBACK-CHECKLIST.txt") `
    -Value $feedbackChecklist `
    -Encoding UTF8

Set-Content `
    -LiteralPath (Join-Path $stagingDirectory "1-READ-ME-FIRST.html") `
    -Value $installationGuide `
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

    $requiredEntries = @(
        "$bundleName/1-READ-ME-FIRST.html",
        "$bundleName/$bundleInstallerName",
        "$bundleName/Beta-Information/BETA-README.txt",
        "$bundleName/Beta-Information/BETA-FEEDBACK-CHECKLIST.txt",
        "$bundleName/Beta-Information/$bundleInstallerName.sha256"
    )

    foreach ($requiredEntry in $requiredEntries) {
        if ($entryNames -notcontains $requiredEntry) {
            throw "Controlled-beta ZIP is missing: $requiredEntry"
        }
    }

    $fileEntries = @($entryNames | Where-Object { -not $_.EndsWith('/') })
    $unexpectedEntries = @(
        $fileEntries | Where-Object { $requiredEntries -notcontains $_ }
    )

    if ($fileEntries.Count -ne $requiredEntries.Count -or
        $unexpectedEntries.Count -ne 0) {
        throw "Controlled-beta ZIP must contain exactly five documented files."
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

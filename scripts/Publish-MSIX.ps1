param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^\d+\.\d+\.\d+\.\d+$")]
    [string]$PackageVersion,

    [Parameter(Mandatory = $true)]
    [string]$PackageIdentityName,

    [Parameter(Mandatory = $true)]
    [string]$Publisher,
    [Parameter(Mandatory = $true)]
    [string]$PublisherDisplayName,

    [string]$CertificateThumbprint,

    [switch]$SkipSigning,

    [switch]$AllowDirtyWorkingTree
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot

$solution =
    Join-Path $repo "SystemPerformanceAccelerator.slnx"

$desktopProject =
    Join-Path `
        $repo `
        "src\SystemPerformanceAccelerator.Desktop\SystemPerformanceAccelerator.Desktop.csproj"

$helperProject =
    Join-Path `
        $repo `
        "src\SystemPerformanceAccelerator.PrivilegedHelper\SystemPerformanceAccelerator.PrivilegedHelper.csproj"

$manifestTemplate =
    Join-Path $repo "packaging\msix\AppxManifest.xml"

$trackedAssets =
    Join-Path $repo "packaging\msix\Assets"

$runtimeIdentifier = "win-x64"

$root =
    Join-Path `
        $repo `
        "artifacts\msix"

$desktopPublish =
    Join-Path $root "desktop-publish"

$helperPublish =
    Join-Path $root "helper-publish"

$staging =
    Join-Path $root "staging"

$verification =
    Join-Path $root "verification"

$output =
    Join-Path $root "output"

$packagePath =
    Join-Path `
        $output `
        "PC-SPA-$PackageVersion-x64.msix"

$windowsSdkRoot =
    "C:\Program Files (x86)\Windows Kits\10\bin"

$makeAppx =
    Get-ChildItem `
        -LiteralPath $windowsSdkRoot `
        -Filter "makeappx.exe" `
        -File `
        -Recurse |
    Where-Object {
        $_.FullName -match "\\x64\\makeappx\.exe$" -or
        $_.FullName -match "\\x86\\makeappx\.exe$"
    } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

$signTool =
    Get-ChildItem `
        -LiteralPath $windowsSdkRoot `
        -Filter "signtool.exe" `
        -File `
        -Recurse |
    Where-Object {
        $_.FullName -match "\\x64\\signtool\.exe$" -or
        $_.FullName -match "\\x86\\signtool\.exe$"
    } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if ($null -eq $makeAppx) {
    throw "MakeAppx.exe was not found in the installed Windows SDK."
}

if (-not $SkipSigning -and $null -eq $signTool) {
    throw "SignTool.exe was not found in the installed Windows SDK."
}

Set-Location -LiteralPath $repo

$changes =
    @(git status --porcelain=v1 -uall)

if (-not $AllowDirtyWorkingTree -and $changes.Count -ne 0) {
    $changes
    throw "MSIX packaging must run from a clean working tree."
}

$sourceCommit =
    (git rev-parse HEAD).Trim()

Get-Process `
    -Name "PC-SPA" `
    -ErrorAction SilentlyContinue |
    Stop-Process -Force

Remove-Item `
    -LiteralPath $root `
    -Recurse `
    -Force `
    -ErrorAction SilentlyContinue

New-Item `
    -ItemType Directory `
    -Path $desktopPublish `
    -Force |
    Out-Null

New-Item `
    -ItemType Directory `
    -Path $helperPublish `
    -Force |
    Out-Null

New-Item `
    -ItemType Directory `
    -Path $staging `
    -Force |
    Out-Null

New-Item `
    -ItemType Directory `
    -Path $verification `
    -Force |
    Out-Null

New-Item `
    -ItemType Directory `
    -Path $output `
    -Force |
    Out-Null

Write-Host ""
Write-Host "Clean Release build..." -ForegroundColor Cyan

dotnet clean `
    $solution `
    -c Release `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Release clean failed."
}

dotnet build `
    $solution `
    -c Release `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Release build failed."
}

Write-Host ""
Write-Host "Full test suite..." -ForegroundColor Cyan

dotnet test `
    $solution `
    -c Release `
    --no-build `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Tests failed."
}

Write-Host ""
Write-Host "Fresh desktop publish..." -ForegroundColor Cyan

dotnet publish `
    $desktopProject `
    -c Release `
    -r $runtimeIdentifier `
    --self-contained true `
    -p:PublishProfile=Windows-x64-Portable `
    -o $desktopPublish

if ($LASTEXITCODE -ne 0) {
    throw "Desktop publish failed."
}

Write-Host ""
Write-Host "Fresh privileged helper publish..." -ForegroundColor Cyan

dotnet publish `
    $helperProject `
    -c Release `
    -r $runtimeIdentifier `
    --self-contained true `
    -p:UseAppHost=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $helperPublish

if ($LASTEXITCODE -ne 0) {
    throw "Privileged helper publish failed."
}

$helperArtifacts = @(
    "PC-SPA.PrivilegedHelper.exe",
    "PC-SPA.PrivilegedHelper.dll",
    "PC-SPA.PrivilegedHelper.deps.json",
    "PC-SPA.PrivilegedHelper.runtimeconfig.json"
)

foreach ($helperArtifact in $helperArtifacts) {

    $helperSource =
        Join-Path $helperPublish $helperArtifact

    if (-not (Test-Path -LiteralPath $helperSource -PathType Leaf)) {
        throw "Required helper artifact missing: $helperArtifact"
    }

    Copy-Item `
        -LiteralPath $helperSource `
        -Destination (Join-Path $desktopPublish $helperArtifact) `
        -Force
}

$criticalFiles = @(
    "PC-SPA.exe",
    "PC-SPA.dll",
    "SystemPerformanceAccelerator.Infrastructure.dll",
    "PC-SPA.PrivilegedHelper.exe",
    "PC-SPA.PrivilegedHelper.dll",
    "PC-SPA.PrivilegedHelper.deps.json",
    "PC-SPA.PrivilegedHelper.runtimeconfig.json"
)

foreach ($criticalFile in $criticalFiles) {

    $path =
        Join-Path $desktopPublish $criticalFile

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Critical publish file missing: $criticalFile"
    }
}

$pdbFiles =
    @(
        Get-ChildItem `
            -LiteralPath $desktopPublish `
            -Filter "*.pdb" `
            -File `
            -Recurse `
            -ErrorAction SilentlyContinue
    )

foreach ($pdbFile in $pdbFiles) {
    Remove-Item `
        -LiteralPath $pdbFile.FullName `
        -Force
}

$remainingPdbFiles =
    @(
        Get-ChildItem `
            -LiteralPath $desktopPublish `
            -Filter "*.pdb" `
            -File `
            -Recurse `
            -ErrorAction SilentlyContinue
    )

if ($remainingPdbFiles.Count -ne 0) {
    $remainingPdbFiles |
        ForEach-Object {
            Write-Host $_.FullName
        }

    throw "Debug-symbol files remain in MSIX publish output."
}

Write-Host ""
Write-Host "Staging fresh publish..." -ForegroundColor Cyan

Get-ChildItem `
    -LiteralPath $desktopPublish `
    -Force |
    Copy-Item `
        -Destination $staging `
        -Recurse `
        -Force

Copy-Item `
    -LiteralPath $trackedAssets `
    -Destination $staging `
    -Recurse `
    -Force

$template =
    [System.IO.File]::ReadAllText(
        $manifestTemplate
    )

$resolvedManifest =
    $template.
        Replace(
            "__PACKAGE_IDENTITY_NAME__",
            $PackageIdentityName
        ).
        Replace(
            "__PACKAGE_PUBLISHER__",
            $Publisher
        ).
        Replace(
            "__PACKAGE_PUBLISHER_DISPLAY_NAME__",
            $PublisherDisplayName
        ).
        Replace(
            "__PACKAGE_VERSION__",
            $PackageVersion
        )

[System.IO.File]::WriteAllText(
    (Join-Path $staging "AppxManifest.xml"),
    $resolvedManifest,
    [System.Text.UTF8Encoding]::new($false)
)

Write-Host ""
Write-Host "Verifying staged critical files..." -ForegroundColor Cyan

foreach ($criticalFile in $criticalFiles) {

    $publishFile =
        Join-Path $desktopPublish $criticalFile

    $stagedFile =
        Join-Path $staging $criticalFile

    $publishHash =
        (Get-FileHash `
            -LiteralPath $publishFile `
            -Algorithm SHA256).Hash

    $stagedHash =
        (Get-FileHash `
            -LiteralPath $stagedFile `
            -Algorithm SHA256).Hash

    if ($publishHash -ne $stagedHash) {
        throw "Staged hash mismatch: $criticalFile"
    }
}

& $makeAppx.FullName `
    pack `
    /d $staging `
    /p $packagePath `
    /o

if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed."
}

if (-not $SkipSigning) {

    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw "CertificateThumbprint is required unless -SkipSigning is used."
    }

    & $signTool.FullName `
        sign `
        /fd SHA256 `
        /sha1 $CertificateThumbprint `
        /s My `
        $packagePath

    if ($LASTEXITCODE -ne 0) {
        throw "MSIX signing failed."
    }

    & $signTool.FullName `
        verify `
        /pa `
        $packagePath

    if ($LASTEXITCODE -ne 0) {
        throw "MSIX signature verification failed."
    }
}

& $makeAppx.FullName `
    unpack `
    /p $packagePath `
    /d $verification `
    /o

if ($LASTEXITCODE -ne 0) {
    throw "MSIX unpack verification failed."
}

foreach ($criticalFile in $criticalFiles) {

    $publishFile =
        Join-Path $desktopPublish $criticalFile

    $verifiedFile =
        Join-Path $verification $criticalFile

    if (-not (Test-Path -LiteralPath $verifiedFile -PathType Leaf)) {
        throw "Critical packaged file missing: $criticalFile"
    }

    $publishHash =
        (Get-FileHash `
            -LiteralPath $publishFile `
            -Algorithm SHA256).Hash

    $verifiedHash =
        (Get-FileHash `
            -LiteralPath $verifiedFile `
            -Algorithm SHA256).Hash

    if ($publishHash -ne $verifiedHash) {
        throw "Packaged hash mismatch: $criticalFile"
    }
}

$oldBlocker =
    "Administrator elevation is required for Microsoft DISM and SFC assessment commands."

$infrastructureDll =
    Join-Path `
        $verification `
        "SystemPerformanceAccelerator.Infrastructure.dll"

$assemblyBytes =
    [System.IO.File]::ReadAllBytes(
        $infrastructureDll
    )

$utf8 =
    [System.Text.Encoding]::UTF8.GetString(
        $assemblyBytes
    )

$unicode =
    [System.Text.Encoding]::Unicode.GetString(
        $assemblyBytes
    )

if (
    $utf8.Contains($oldBlocker) -or
    $unicode.Contains($oldBlocker)
) {
    throw "Packaged Infrastructure DLL contains the obsolete elevation blocker."
}

$finalChanges =
    @(git status --porcelain=v1 -uall)

if (-not $AllowDirtyWorkingTree -and $finalChanges.Count -ne 0) {
    $finalChanges
    throw "Repository changed during MSIX packaging."
}

Write-Host ""
Write-Host "============================================================"
Write-Host "PC-SPA MSIX PACKAGE VERIFIED" -ForegroundColor Green
Write-Host "============================================================"
Write-Host "Source commit : $sourceCommit"
Write-Host "Version       : $PackageVersion"
Write-Host "Identity      : $PackageIdentityName"
Write-Host "Package       : $packagePath"
Write-Host "Critical hashes: VERIFIED"
Write-Host "Old blocker    : ABSENT"
if ($AllowDirtyWorkingTree) {
    Write-Host "Repository     : CONTROLLED DIRTY TEST SCOPE"
}
else {
    Write-Host "Repository     : CLEAN"
}
Write-Host "============================================================"
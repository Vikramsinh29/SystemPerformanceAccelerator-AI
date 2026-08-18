param(
    [string]$CertificateThumbprint = $env:PCSPA_SIGNING_CERTIFICATE_THUMBPRINT,

    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$CertificateStoreLocation = "CurrentUser",

    [string]$TimestampUrl = $env:PCSPA_SIGNING_TIMESTAMP_URL,

    [switch]$RequireReady
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $repo "artifacts\publish\win-x64"
$installerPath = Join-Path $repo "artifacts\installer\PC-SPA-1.0.0-win-x64-setup.exe"
$issues = [System.Collections.Generic.List[string]]::new()

if ($env:OS -ne "Windows_NT") {
    throw "Code-signing readiness must be checked on Windows."
}

$signToolCandidates = [System.Collections.Generic.List[string]]::new()

if (-not [string]::IsNullOrWhiteSpace($env:PCSPA_SIGNTOOL_PATH)) {
    $signToolCandidates.Add($env:PCSPA_SIGNTOOL_PATH)
}

$windowsKitsRoots = @(
    (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"),
    (Join-Path $env:ProgramFiles "Windows Kits\10\bin")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

foreach ($windowsKitsRoot in $windowsKitsRoots) {
    if (-not (Test-Path -LiteralPath $windowsKitsRoot -PathType Container)) {
        continue
    }

    $sdkSignTools = @(
        Get-ChildItem -LiteralPath $windowsKitsRoot -Directory |
            Where-Object { $null -ne ($_.Name -as [Version]) } |
            Sort-Object { [Version]$_.Name } -Descending |
            ForEach-Object {
                Join-Path $_.FullName "x64\signtool.exe"
            } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
    )

    foreach ($sdkSignTool in $sdkSignTools) {
        $signToolCandidates.Add($sdkSignTool)
    }
}

$signToolPath = $signToolCandidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($signToolPath)) {
    $issues.Add("Microsoft SignTool x64 was not found. Install a current Windows SDK or set PCSPA_SIGNTOOL_PATH.")
}

$normalizedThumbprint = $CertificateThumbprint -replace '\s', ''
$certificate = $null
$certificateReady = $false

if ([string]::IsNullOrWhiteSpace($normalizedThumbprint)) {
    $issues.Add("PCSPA_SIGNING_CERTIFICATE_THUMBPRINT is not configured.")
}
elseif ($normalizedThumbprint -notmatch '^[0-9A-Fa-f]{40}$') {
    $issues.Add("The signing-certificate thumbprint must contain exactly 40 hexadecimal characters.")
}
else {
    $certificatePath = "Cert:\$CertificateStoreLocation\My\$normalizedThumbprint"
    $certificate = Get-Item -LiteralPath $certificatePath -ErrorAction SilentlyContinue

    if ($null -eq $certificate) {
        $issues.Add("The configured signing certificate was not found in $CertificateStoreLocation\\My.")
    }
    else {
        $hasCodeSigningEku = $certificate.EnhancedKeyUsageList.ObjectId.Value -contains `
            "1.3.6.1.5.5.7.3.3"

        if (-not $certificate.HasPrivateKey) {
            $issues.Add("The configured signing certificate has no accessible private key.")
        }

        if (-not $hasCodeSigningEku) {
            $issues.Add("The configured certificate is not valid for code signing.")
        }

        if ($certificate.NotBefore -gt [DateTime]::Now) {
            $issues.Add("The configured signing certificate is not valid yet.")
        }

        if ($certificate.NotAfter -le [DateTime]::Now) {
            $issues.Add("The configured signing certificate has expired.")
        }

        $certificateReady = $certificate.HasPrivateKey -and
            $hasCodeSigningEku -and
            $certificate.NotBefore -le [DateTime]::Now -and
            $certificate.NotAfter -gt [DateTime]::Now
    }
}

$timestampReady = $false
$timestampUri = $null

if ([string]::IsNullOrWhiteSpace($TimestampUrl)) {
    $issues.Add("PCSPA_SIGNING_TIMESTAMP_URL is not configured.")
}
elseif (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestampUri) -or
    $timestampUri.Scheme -notin @("http", "https")) {
    $issues.Add("The timestamp URL must be an absolute HTTP or HTTPS URL.")
}
else {
    $timestampReady = $true
}

$artifactPaths = [System.Collections.Generic.List[string]]::new()

if (Test-Path -LiteralPath $publishDirectory -PathType Container) {
    foreach ($artifactName in @(
        "PC-SPA.exe",
        "PC-SPA.dll",
        "PC-SPA.PrivilegedHelper.exe",
        "PC-SPA.PrivilegedHelper.dll",
        "SystemPerformanceAccelerator.Core.dll",
        "SystemPerformanceAccelerator.Infrastructure.dll"
    )) {
        $artifactPath = Join-Path $publishDirectory $artifactName
        if (Test-Path -LiteralPath $artifactPath -PathType Leaf) {
            $artifactPaths.Add($artifactPath)
        }
        else {
            $issues.Add("Expected published PE file is missing: $artifactPath")
        }
    }
}
else {
    $issues.Add("The verified Windows x64 publish directory is missing: $publishDirectory")
}

if (Test-Path -LiteralPath $installerPath -PathType Leaf) {
    $artifactPaths.Add($installerPath)
}
else {
    $issues.Add("The Windows x64 installer is missing: $installerPath")
}

$artifactSignatures = @(
    $artifactPaths |
        ForEach-Object {
            $signature = Get-AuthenticodeSignature -LiteralPath $_
            [PSCustomObject]@{
                Path = $_
                Status = $signature.Status
                Signer = if ($null -eq $signature.SignerCertificate) {
                    $null
                }
                else {
                    $signature.SignerCertificate.Subject
                }
            }
        }
)

$invalidSignatureCount = @(
    $artifactSignatures | Where-Object { $_.Status -ne "Valid" }
).Count

$ready = -not [string]::IsNullOrWhiteSpace($signToolPath) -and
    $certificateReady -and
    $timestampReady -and
    $issues.Count -eq 0

$result = [PSCustomObject]@{
    Ready = $ready
    SignToolPath = $signToolPath
    CertificateStore = "$CertificateStoreLocation\My"
    CertificateSubject = if ($null -eq $certificate) { $null } else { $certificate.Subject }
    CertificateExpires = if ($null -eq $certificate) { $null } else { $certificate.NotAfter }
    TimestampUrl = $TimestampUrl
    AuditedArtifactCount = $artifactSignatures.Count
    InvalidSignatureCount = $invalidSignatureCount
    Issues = @($issues)
}

$result
$artifactSignatures | Format-Table -AutoSize

if ($RequireReady -and -not $ready) {
    throw "Code-signing readiness failed: $($issues -join ' ')"
}

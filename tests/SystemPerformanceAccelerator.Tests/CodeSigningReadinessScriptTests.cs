using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class CodeSigningReadinessScriptTests
{
    [Fact]
    public void ReadinessScript_UsesCertificateStoreWithoutPersistingSecrets()
    {
        var script = ReadScript();

        Assert.Contains("PCSPA_SIGNING_CERTIFICATE_THUMBPRINT", script);
        Assert.Contains("CertificateStoreLocation", script);
        Assert.Contains("HasPrivateKey", script);
        Assert.Contains("1.3.6.1.5.5.7.3.3", script);
        Assert.DoesNotContain(".pfx", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadinessScript_RequiresSha256CapableSigningPrerequisites()
    {
        var script = ReadScript();

        Assert.Contains("PCSPA_SIGNTOOL_PATH", script);
        Assert.Contains("Windows Kits\\10\\bin", script);
        Assert.Contains("x64\\signtool.exe", script);
        Assert.Contains("PCSPA_SIGNING_TIMESTAMP_URL", script);
        Assert.Contains("http", script);
        Assert.Contains("https", script);
    }

    [Fact]
    public void ReadinessScript_AuditsOnlyPcSpaOwnedReleaseArtifacts()
    {
        var script = ReadScript();

        Assert.Contains("PC-SPA.exe", script);
        Assert.Contains("PC-SPA.dll", script);
        Assert.Contains("SystemPerformanceAccelerator.Core.dll", script);
        Assert.Contains("SystemPerformanceAccelerator.Infrastructure.dll", script);
        Assert.Contains("PC-SPA-1.0.0-win-x64-setup.exe", script);
        Assert.DoesNotContain("PC-SPA-1.0.0-beta.1-win-x64-setup.exe", script);
        Assert.Contains("Get-AuthenticodeSignature", script);
    }

    [Fact]
    public void ReadinessScript_CanFailClosedWithoutSigningFiles()
    {
        var script = ReadScript();

        Assert.Contains("[switch]$RequireReady", script);
        Assert.Contains("Code-signing readiness failed", script);
        Assert.DoesNotContain("signtool.exe\" sign", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Set-AuthenticodeSignature", script);
        Assert.DoesNotContain("Invoke-WebRequest", script);
    }

    private static string ReadScript()
    {
        return File.ReadAllText(FindRepositoryFile(
            "scripts",
            "Test-Windows-CodeSigningReadiness.ps1"));
    }

    private static string FindRepositoryFile(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{Path.Combine(relativePath)}'.");
    }
}

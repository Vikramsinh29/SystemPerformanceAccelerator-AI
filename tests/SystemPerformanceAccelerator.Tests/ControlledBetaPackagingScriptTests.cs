using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class ControlledBetaPackagingScriptTests
{
    [Fact]
    public void BetaPublisher_ReusesVerifiedInstallerAndChecksIntegrity()
    {
        var script = ReadScript();

        Assert.Contains("Publish-Windows-x64-Installer.ps1", script);
        Assert.Contains("Get-FileHash", script);
        Assert.Contains("Installer SHA-256 does not match", script);
        Assert.Contains("Get-AuthenticodeSignature", script);
    }

    [Fact]
    public void BetaPublisher_IncludesInstructionsAndFeedbackChecklist()
    {
        var script = ReadScript();

        Assert.Contains("BETA-README.txt", script);
        Assert.Contains("BETA-FEEDBACK-CHECKLIST.txt", script);
        Assert.Contains("Unknown Publisher", script);
        Assert.Contains("never automatically restarts Windows", script);
        Assert.Contains("Do not fabricate repair results", script);
        Assert.Contains("never uploads diagnostic evidence automatically", script);
    }

    [Fact]
    public void BetaPublisher_CreatesLocalBundleWithoutExternalPublication()
    {
        var script = ReadScript();

        Assert.Contains("PC-SPA-$version-win-x64-controlled-beta", script);
        Assert.Contains("$bundleName.zip", script);
        Assert.Contains("Compress-Archive", script);
        Assert.Contains("Invited beta testers only", script);
        Assert.DoesNotContain("git push", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-WebRequest", script);
        Assert.DoesNotContain("gh release", script, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadScript()
    {
        return File.ReadAllText(FindRepositoryFile(
            "scripts",
            "Publish-Controlled-Beta.ps1"));
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

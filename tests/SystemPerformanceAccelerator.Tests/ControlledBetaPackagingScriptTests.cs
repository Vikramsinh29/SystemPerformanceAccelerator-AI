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
        Assert.Contains("1-READ-ME-FIRST.html", script);
        Assert.Contains("2-INSTALL-PC-SPA.exe", script);
        Assert.Contains("Beta-Information", script);
        Assert.Contains("font-family: \"Segoe UI\"", script);
        Assert.Contains("PC-SPA-Exact-Original-2048x2048.png", script);
        Assert.Contains("guideLogoBase64", script);
        Assert.Contains("data:image/png;base64", script);
        Assert.Contains("aspect-ratio: 1.43 / 1", script);
        Assert.Contains("@media (max-width: 680px)", script);
        Assert.Contains("background: #02090d", script);
        Assert.Contains("transform: translateY(-15.5%)", script);
        Assert.DoesNotContain("guidePhoenixBase64", script);
        Assert.DoesNotContain("guideWordmarkBase64", script);
        Assert.Contains("Required installation-guide branding asset is missing", script);
        Assert.Contains("This page contains no scripts, tracking, or network content", script);
        Assert.Contains("More info", script);
        Assert.Contains("Unknown Publisher", script);
        Assert.Contains("never automatically restarts Windows", script);
        Assert.Contains("Do not fabricate repair results", script);
        Assert.Contains("never uploads diagnostic evidence automatically", script);
        Assert.Contains("Beta Error Feedback is the only optional network feature", script);
        Assert.Contains("Personal files and file contents are never attached or uploaded", script);
        Assert.Contains("Nothing is sent automatically", script);
        Assert.Contains("local ZIP fallback was offered", script);
        Assert.Contains("exactly five documented files", script);
        Assert.Contains("$installerArguments.SkipDesktopCopy = $true", script);
        Assert.Contains("2026-08-07T00:00:00Z", script);
        Assert.Contains("$betaReleaseUtc.AddDays(30)", script);
        Assert.Contains("No account or activation key is required", script);
        Assert.Contains("Beta release metadata does not match", script);
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

using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class ReleasePackagingScriptTests
{
    [Fact]
    public void WindowsPublishScript_RequiresElevationBeforeReleaseWork()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts",
            "Publish-Windows-x64.ps1"));

        Assert.Contains("WindowsBuiltInRole]::Administrator", script);
        Assert.Contains(
            "Release verification must run from an elevated PowerShell window.",
            script);
    }

    [Fact]
    public void WindowsPublishScript_VerifiesExtractedExecutableLaunchAndNormalClose()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts",
            "Publish-Windows-x64.ps1"));

        Assert.Contains("Expand-Archive", script);
        Assert.Contains("$extractedExecutable", script);
        Assert.Contains("*System Performance Accelerator", script);
        Assert.Contains("$launchProcess.CloseMainWindow()", script);
        Assert.Contains("$launchProcess.WaitForExit(10000)", script);
        Assert.Contains("Extracted portable launch: Passed", script);
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

using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class PrivilegedHelperOutputStagingTests
{
    [Fact]
    public void DesktopProject_StagesPrivilegedHelperForBuildAndPublish()
    {
        var repoRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repoRoot,
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "SystemPerformanceAccelerator.Desktop.csproj");
        var text = File.ReadAllText(projectPath);

        Assert.Contains(
            "SystemPerformanceAccelerator.PrivilegedHelper.csproj",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "ReferenceOutputAssembly=\"false\"",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "CopyPrivilegedHelperToBuildOutput",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "CopyPrivilegedHelperToPublishOutput",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "PC-SPA.PrivilegedHelper.*",
            text,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "SystemPerformanceAccelerator.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Repository root could not be located from the test runtime directory.");
    }
}

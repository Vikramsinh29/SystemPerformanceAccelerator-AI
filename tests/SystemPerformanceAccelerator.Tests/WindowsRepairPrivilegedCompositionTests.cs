using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsRepairPrivilegedCompositionTests
{
    [Fact]
    public void MainWindow_ComposesGuidedRepairThroughPrivilegedBoundary()
    {
        var source = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "MainWindow.xaml.cs");

        Assert.Contains(
            "new WindowsPrivilegedOperationExecutor()",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "new PrivilegedWindowsRepairExecutionCommandRunner(",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "new WindowsRepairExecutionCommandRunner(),\n                windowsRepairAssessmentService",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopManifest_RemainsElevatedUntilReadinessModelIsRefactored()
    {
        var manifest = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "app.manifest");

        Assert.Contains(
            "requestedExecutionLevel level=\"requireAdministrator\"",
            manifest,
            StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{Path.Combine(relativePath)}'.");
    }
}

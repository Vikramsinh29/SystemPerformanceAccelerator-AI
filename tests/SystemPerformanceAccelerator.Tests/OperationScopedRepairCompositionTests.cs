using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class OperationScopedRepairCompositionTests
{
    [Fact]
    public void MainWindow_ComposesOperationScopedPlanningAndPrivilegedRepairExecution()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "MainWindow.xaml.cs"));

        Assert.Contains(
            "new OperationScopedWindowsRepairPlanService(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "new PrivilegedWindowsRepairExecutionCommandRunner(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "new WindowsPrivilegedOperationExecutor()",
            source,
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
            "Repository root could not be located from the test output directory.");
    }
}

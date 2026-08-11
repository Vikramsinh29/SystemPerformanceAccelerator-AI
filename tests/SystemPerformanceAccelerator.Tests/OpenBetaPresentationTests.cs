namespace SystemPerformanceAccelerator.Tests;

public sealed class OpenBetaPresentationTests
{
    [Fact]
    public void MainWindow_UsesOpenBetaPresentationWithoutStartupLicensing()
    {
        var xaml = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "MainWindow.xaml");
        var codeBehind = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "MainWindow.xaml.cs");

        Assert.Contains("OPEN BETA ACCESS", xaml);
        Assert.Contains("No account or activation key is required", xaml);
        Assert.DoesNotContain("Loaded=\"Window_Loaded\"", xaml);
        Assert.DoesNotContain("InitializeBetaAccessAsync", codeBehind);
    }

    [Fact]
    public void MainWindowViewModel_DisablesLicensingGateForBeta()
    {
        var viewModel = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "ViewModels",
            "MainWindowViewModel.cs");

        Assert.Contains(
            "public bool IsBetaAccessInitializing => false;",
            viewModel);
        Assert.Contains(
            "public bool IsBetaAccessGateVisible => false;",
            viewModel);
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

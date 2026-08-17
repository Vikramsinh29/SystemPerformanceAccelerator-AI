using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class CommercialReleasePresentationTests
{
    [Fact]
    public void MainWindow_UsesCommercialAccountPresentation()
    {
        var xaml = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "MainWindow.xaml");
        var viewModel = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "ViewModels",
            "MainWindowViewModel.cs");

        Assert.Contains("Account &amp; License", xaml);
        Assert.Contains("COMMERCIAL LICENSING", xaml);
        Assert.Contains("Commercial licensing is not connected in this build", xaml);
        Assert.Contains("Account & License", viewModel);
        Assert.Contains("Commercial licensing is not connected in this build", viewModel);

        Assert.DoesNotContain("OPEN BETA ACCESS", xaml);
        Assert.DoesNotContain("No account or activation key is required", xaml);
        Assert.DoesNotContain("Beta Access", viewModel);
        Assert.DoesNotContain("1.0.0-beta.1", viewModel);
        Assert.DoesNotContain("BetaBuildPolicy", viewModel);
    }

    [Fact]
    public void Desktop_DoesNotRestoreLegacyCredentialControls()
    {
        var xaml = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "MainWindow.xaml");
        var viewModel = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "ViewModels",
            "MainWindowViewModel.cs");

        Assert.DoesNotContain("PasswordBox", xaml);
        Assert.DoesNotContain("ActivateBetaAccessCommand", xaml);
        Assert.DoesNotContain("IsBetaAccessGateVisible", viewModel);
        Assert.DoesNotContain("IAuthenticationService", viewModel);
        Assert.DoesNotContain("ILicenseActivationService", viewModel);
    }

    [Theory]
    [InlineData("IAuthenticationService.cs")]
    [InlineData("ILicenseActivationService.cs")]
    [InlineData("ISecureTokenStorage.cs")]
    [InlineData("AuthenticationService.cs")]
    [InlineData("LicenseActivationService.cs")]
    [InlineData("FileSecureTokenStorage.cs")]
    public void LegacyLicensingRuntimeFile_IsAbsent(string fileName)
    {
        Assert.False(RepositoryContainsFile(fileName));
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

    private static bool RepositoryContainsFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SystemPerformanceAccelerator.slnx")))
            {
                return Directory.EnumerateFiles(
                        directory.FullName,
                        fileName,
                        SearchOption.AllDirectories)
                    .Any(path =>
                        !path.Contains(
                            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase) &&
                        !path.Contains(
                            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

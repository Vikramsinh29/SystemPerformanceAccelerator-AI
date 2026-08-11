using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class HelpCenterPresentationTests
{
    [Fact]
    public void MainWindow_ProvidesSeparateHelpNavigationAndSearchableGuides()
    {
        var xaml = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "MainWindow.xaml");

        Assert.Contains("Text=\"HELP\"", xaml);
        Assert.Contains("Text=\"Help &amp; Guides\"", xaml);
        Assert.Contains("ShowHelpCommand", xaml);
        Assert.Contains("IsHelpContentVisible", xaml);
        Assert.Contains("WHAT DO YOU NEED HELP WITH?", xaml);
        Assert.Contains("ItemsSource=\"{Binding FilteredGuides}\"", xaml);
        Assert.Contains("Content=\"Open this tool\"", xaml);
        Assert.Contains("Safety first", xaml);
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

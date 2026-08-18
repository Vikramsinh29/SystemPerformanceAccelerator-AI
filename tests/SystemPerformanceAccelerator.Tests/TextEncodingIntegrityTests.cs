using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class TextEncodingIntegrityTests
{
    private static readonly string[] MojibakeMarkers =
    [
        "â€¢",
        "â€”",
        "â€“",
        "â†",
        "Â"
    ];

    [Theory]
    [InlineData("src", "SystemPerformanceAccelerator.Desktop", "MainWindow.xaml")]
    [InlineData("src", "SystemPerformanceAccelerator.Desktop", "ViewModels", "MainWindowViewModel.cs")]
    public void CustomerFacingDesktopText_DoesNotContainKnownMojibake(params string[] relativePath)
    {
        var content = ReadRepositoryFile(relativePath);

        foreach (var marker in MojibakeMarkers)
        {
            Assert.DoesNotContain(marker, content, StringComparison.Ordinal);
        }
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

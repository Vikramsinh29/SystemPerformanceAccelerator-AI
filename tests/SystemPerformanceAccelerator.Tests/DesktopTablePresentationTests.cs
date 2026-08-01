using System.Xml.Linq;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class DesktopTablePresentationTests
{
    private static readonly string[] ResultGridNames =
    [
        "HealthFindingsGrid",
        "CleanerResultsGrid",
        "CustomCleanResultsGrid",
        "AutoCleanRunResultsGrid",
        "LargeFilesGrid",
        "DuplicateFilesGrid",
        "StartupItemsGrid",
        "WindowsRepairResultsGrid",
        "WindowsRepairPastRecordsGrid"
    ];

    [Fact]
    public void ResultTables_UseSharedPresentationWithoutLocalSizingOrGridLineOverrides()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var grids = document
            .Descendants()
            .Where(element => element.Name.LocalName == "DataGrid")
            .Where(element => ResultGridNames.Contains((string?)element.Attribute(xaml + "Name")))
            .ToDictionary(element => (string)element.Attribute(xaml + "Name")!);

        Assert.Equal(ResultGridNames.Length, grids.Count);

        foreach (var gridName in ResultGridNames)
        {
            var grid = grids[gridName];

            Assert.Contains("FluentDataGridStyle", (string?)grid.Attribute("Style"));
            Assert.Null(grid.Attribute("GridLinesVisibility"));
            Assert.Null(grid.Attribute("ColumnHeaderHeight"));
            Assert.Null(grid.Attribute("RowHeight"));
        }
    }

    [Fact]
    public void SharedTableStyle_UsesSingleRowSeparatorsAndUniformSizing()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "Resources",
            "Tables.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var styles = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Style" &&
                              element.Attribute(xaml + "Key") is not null)
            .ToDictionary(element => (string)element.Attribute(xaml + "Key")!);

        AssertSetter(styles["FluentDataGridStyle"], "GridLinesVisibility", "None");
        AssertSetter(styles["FluentDataGridColumnHeaderStyle"], "Height", "42");
        AssertSetter(styles["FluentDataGridRowStyle"], "Height", "40");
        AssertSetter(styles["FluentDataGridRowStyle"], "BorderThickness", "0,0,0,1");
    }

    [Fact]
    public void LargeAndDuplicateActivityBars_UseSharedFullTrackAnimation()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var name in new[]
                 {
                     "LargeFileActivityProgressBar",
                     "DuplicateActivityProgressBar"
                 })
        {
            var progressBar = Assert.Single(
                document.Descendants(),
                element => element.Name.LocalName == "ProgressBar" &&
                           (string?)element.Attribute(xaml + "Name") == name);

            Assert.Contains(
                "FullTrackActivityProgressBarStyle",
                (string)progressBar.Attribute("Style")!);
        }
    }

    private static void AssertSetter(XElement style, string property, string value)
    {
        Assert.Contains(
            style.Elements().Where(element => element.Name.LocalName == "Setter"),
            setter => (string?)setter.Attribute("Property") == property &&
                      (string?)setter.Attribute("Value") == value);
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

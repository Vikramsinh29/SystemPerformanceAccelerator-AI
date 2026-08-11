using System.Windows.Input;
using SystemPerformanceAccelerator.Desktop.Commands;
using SystemPerformanceAccelerator.Desktop.ViewModels;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class HelpCenterViewModelTests
{
    [Fact]
    public void Guides_CoverEveryDesktopToolAndStartUnfiltered()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(9, viewModel.Guides.Count);
        Assert.Equal("9 guides", viewModel.VisibleGuideCountText);
        Assert.True(viewModel.HasVisibleGuides);
        Assert.False(viewModel.HasNoVisibleGuides);
        Assert.Contains(
            viewModel.Guides,
            guide => guide.ToolName == "Windows Repair");
        Assert.Contains(
            viewModel.Guides,
            guide => guide.ToolName == "System Monitor");
    }

    [Fact]
    public void Search_FiltersAcrossProblemToolAndGuidanceText()
    {
        var viewModel = CreateViewModel();

        viewModel.SearchText = "startup";

        var result = Assert.Single(
            viewModel.FilteredGuides.Cast<ToolHelpGuideViewModel>());
        Assert.Equal("Startup Manager", result.ToolName);
        Assert.Equal("1 guide", viewModel.VisibleGuideCountText);
    }

    [Fact]
    public void Search_WithNoMatch_ExposesEmptyGuidanceState()
    {
        var viewModel = CreateViewModel();

        viewModel.SearchText = "not-a-pc-spa-problem";

        Assert.False(viewModel.HasVisibleGuides);
        Assert.True(viewModel.HasNoVisibleGuides);
        Assert.Equal("0 guides", viewModel.VisibleGuideCountText);
    }

    [Fact]
    public void ClearSearch_RestoresAllGuides()
    {
        var viewModel = CreateViewModel();
        viewModel.SearchText = "repair";

        viewModel.ClearSearchCommand.Execute(null);

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.Equal(9, viewModel.FilteredGuides.Cast<object>().Count());
    }

    private static HelpCenterViewModel CreateViewModel()
    {
        ICommand command = new RelayCommand(() => { });
        return new HelpCenterViewModel(
            command,
            command,
            command,
            command,
            command,
            command,
            command,
            command,
            command);
    }
}

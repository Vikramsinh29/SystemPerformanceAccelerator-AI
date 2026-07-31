using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Services;
using SystemPerformanceAccelerator.Desktop.ViewModels;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class StartupManagerViewModelTests
{
    [Fact]
    public void Summary_IncludesEnabledDisabledAndUnknownRows()
    {
        var viewModel = CreateViewModel();
        viewModel.Results.Add(new StartupItemRowViewModel(
            CreateItem("Enabled tool", StartupItemState.Enabled)));
        viewModel.Results.Add(new StartupItemRowViewModel(
            CreateItem("Disabled tool", StartupItemState.Disabled)));
        viewModel.Results.Add(new StartupItemRowViewModel(
            CreateItem("Unknown tool", StartupItemState.Unknown)));

        Assert.Equal("3", viewModel.ItemsFound);
        Assert.Equal("1", viewModel.EnabledItems);
        Assert.Equal("1", viewModel.DisabledItems);
        Assert.Equal("1", viewModel.UnknownItems);
    }

    [Fact]
    public void RowPresentation_UsesClearStateLabelsAndActions()
    {
        var enabled = new StartupItemRowViewModel(
            CreateItem("Enabled tool", StartupItemState.Enabled));
        var disabled = new StartupItemRowViewModel(
            CreateItem("Disabled tool", StartupItemState.Disabled));
        var unknown = new StartupItemRowViewModel(
            CreateItem("Unknown tool", StartupItemState.Unknown));

        Assert.Equal("Enabled", enabled.StateLabel);
        Assert.Equal(StartupItemState.Disabled, enabled.RequestedState);
        Assert.True(enabled.CanToggle);
        Assert.Contains("Click to disable", enabled.StateActionToolTip);

        Assert.Equal("Disabled", disabled.StateLabel);
        Assert.Equal(StartupItemState.Enabled, disabled.RequestedState);
        Assert.True(disabled.CanToggle);
        Assert.Contains("Click to enable", disabled.StateActionToolTip);

        Assert.Equal("Unknown", unknown.StateLabel);
        Assert.Equal(StartupItemState.Unknown, unknown.RequestedState);
        Assert.False(unknown.CanToggle);
    }

    [Fact]
    public async Task ToggleCommand_ChangesOnlyRequestedRowAndRefreshesInventory()
    {
        var initial = CreateItem("Test tool", StartupItemState.Enabled);
        var startupService = new FakeStartupItemService(initial);
        var viewModel = CreateViewModel(
            startupService,
            new FakeConfirmationService(confirmed: true));
        var row = new StartupItemRowViewModel(initial);
        viewModel.Results.Add(row);

        await viewModel.ToggleItemStateCommand.ExecuteAsync(row);

        Assert.Equal(1, startupService.StateChangeCount);
        Assert.Equal(StartupItemState.Disabled, startupService.LastRequestedState);
        Assert.Single(viewModel.Results);
        Assert.Equal(StartupItemState.Disabled, viewModel.Results[0].State);
        Assert.Equal("0", viewModel.EnabledItems);
        Assert.Equal("1", viewModel.DisabledItems);
        Assert.Contains("Inventory refreshed", viewModel.Status);
    }

    [Fact]
    public async Task ToggleCommand_WhenConfirmationDeclined_MakesNoChange()
    {
        var initial = CreateItem("Test tool", StartupItemState.Enabled);
        var startupService = new FakeStartupItemService(initial);
        var viewModel = CreateViewModel(
            startupService,
            new FakeConfirmationService(confirmed: false));
        var row = new StartupItemRowViewModel(initial);
        viewModel.Results.Add(row);

        await viewModel.ToggleItemStateCommand.ExecuteAsync(row);

        Assert.Equal(0, startupService.StateChangeCount);
        Assert.Equal(StartupItemState.Enabled, row.State);
        Assert.Equal("Startup disable not started.", viewModel.Status);
    }

    private static StartupManagerViewModel CreateViewModel(
        IStartupItemService? startupItemService = null,
        IStartupItemConfirmationService? confirmationService = null) =>
        new(
            startupItemService ?? new FakeStartupItemService(),
            new AllowFeatureAccessGuard(),
            confirmationService ?? new FakeConfirmationService(confirmed: true));

    private static StartupItem CreateItem(
        string name,
        StartupItemState state) =>
        new(
            name,
            $"C:\\Tools\\{name}.exe",
            "Registry Run — Current user (64-bit)",
            "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run",
            state,
            StartupTargetState.Available)
        {
            Kind = StartupItemKind.RegistryRun,
            SourceScope = StartupItemScope.CurrentUser,
            SourceRegistryView = StartupRegistryView.Registry64,
            EntryIdentifier = name,
            ApprovalScope = StartupItemScope.CurrentUser,
            ApprovalRegistryView = StartupRegistryView.Registry64,
            ApprovalCategory = "Run"
        };

    private sealed class FakeStartupItemService : IStartupItemService
    {
        private StartupItem? _currentItem;

        public FakeStartupItemService(StartupItem? initialItem = null)
        {
            _currentItem = initialItem;
        }

        public int StateChangeCount { get; private set; }

        public StartupItemState LastRequestedState { get; private set; } =
            StartupItemState.Unknown;

        public Task<StartupItemScanResult> ScanAsync(
            IProgress<StartupItemScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new StartupItemScanProgress(1, 1, "Test"));
            IReadOnlyList<StartupItem> items = _currentItem is { } currentItem
                ? [currentItem]
                : [];
            return Task.FromResult(new StartupItemScanResult(
                items,
                [],
                1,
                TimeSpan.FromMilliseconds(10)));
        }

        public Task<StartupItemStateChangeResult> SetStateAsync(
            StartupItem item,
            StartupItemState requestedState,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StateChangeCount++;
            LastRequestedState = requestedState;
            _currentItem = item with { State = requestedState };
            return Task.FromResult(new StartupItemStateChangeResult(
                StartupItemStateChangeOutcome.Changed,
                requestedState,
                $"Startup item '{item.Name}' was {(requestedState == StartupItemState.Enabled ? "enabled" : "disabled")}."));
        }
    }

    private sealed class FakeConfirmationService(bool confirmed) :
        IStartupItemConfirmationService
    {
        public bool ConfirmStateChange(
            StartupItem item,
            StartupItemState requestedState) => confirmed;
    }

    private sealed class AllowFeatureAccessGuard : IFeatureAccessGuard
    {
        public ApplicationEdition EffectiveEdition => ApplicationEdition.Business;

        public bool IsDevelopmentOverrideActive => false;

        public FeatureAccessResult GetAccess(ApplicationFeature feature) =>
            new(
                feature,
                EffectiveEdition,
                FeatureAccessState.Available,
                null,
                "Available");

        public bool CanAccess(
            ApplicationFeature feature,
            FeatureAccessRequirement requirement) => true;
    }
}

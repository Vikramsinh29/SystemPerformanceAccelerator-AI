namespace SystemPerformanceAccelerator.Core.Models;

public sealed record StartupItemScanProgress(
    int LocationsScanned,
    int TotalLocations,
    string CurrentLocation);

public sealed record StartupItemScanResult(
    IReadOnlyList<StartupItem> Items,
    IReadOnlyList<string> Errors,
    int LocationsScanned,
    TimeSpan Elapsed)
{
    public int EnabledCount => Items.Count(item => item.State == StartupItemState.Enabled);

    public int DisabledCount => Items.Count(item => item.State == StartupItemState.Disabled);

    public int UnknownCount => Items.Count(item => item.State == StartupItemState.Unknown);
}

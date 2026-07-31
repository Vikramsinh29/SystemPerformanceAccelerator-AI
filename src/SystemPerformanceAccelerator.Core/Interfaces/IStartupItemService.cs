using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IStartupItemService
{
    Task<StartupItemScanResult> ScanAsync(
        IProgress<StartupItemScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<StartupItemStateChangeResult> SetStateAsync(
        StartupItem item,
        StartupItemState requestedState,
        CancellationToken cancellationToken = default);
}

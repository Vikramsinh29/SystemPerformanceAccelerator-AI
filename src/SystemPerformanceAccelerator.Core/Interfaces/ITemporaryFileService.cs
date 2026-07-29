using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface ITemporaryFileService
{
    Task<ScanResult> ScanAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    Task<CleanupResult> CleanAsync(
        IReadOnlyCollection<CleanupCandidate> candidates,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

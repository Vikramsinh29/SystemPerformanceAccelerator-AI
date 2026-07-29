using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface ILargeFileCleanupService
{
    Task<LargeFileCleanupResult> CleanAsync(
        string approvedRootPath,
        IReadOnlyCollection<LargeFileCandidate> candidates,
        IProgress<LargeFileCleanupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

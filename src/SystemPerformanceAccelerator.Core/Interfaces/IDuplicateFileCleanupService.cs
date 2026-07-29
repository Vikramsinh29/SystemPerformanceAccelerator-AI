using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IDuplicateFileCleanupService
{
    Task<DuplicateFileCleanupResult> CleanAsync(
        string approvedRootPath,
        IReadOnlyCollection<DuplicateFileGroup> confirmedGroups,
        IReadOnlyCollection<DuplicateFileCandidate> selectedCandidates,
        IProgress<DuplicateFileCleanupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

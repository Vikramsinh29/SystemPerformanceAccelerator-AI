namespace SystemPerformanceAccelerator.Core.Models;

public sealed record DuplicateFileCleanupProgress(
    int ProcessedCount,
    int TotalCount,
    string CurrentFile);

public sealed record DuplicateFileCleanupResult(
    IReadOnlyList<string> RecycledPaths,
    int SkippedCount,
    IReadOnlyList<string> Errors,
    long ReclaimedBytes,
    TimeSpan Elapsed,
    bool WasCancelled)
{
    public int RecycledCount => RecycledPaths.Count;

    public bool CompletedWithoutErrors =>
        !WasCancelled &&
        SkippedCount == 0 &&
        Errors.Count == 0;
}

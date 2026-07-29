namespace SystemPerformanceAccelerator.Core.Models;

public sealed record LargeFileCleanupProgress(
    int ProcessedCount,
    int TotalCount,
    string CurrentFile);

public sealed record LargeFileCleanupResult(
    IReadOnlyList<string> RecycledPaths,
    IReadOnlyList<string> Errors,
    long ReclaimedBytes,
    TimeSpan Elapsed)
{
    public int RecycledCount => RecycledPaths.Count;
    public int SkippedCount => Errors.Count;
    public bool CompletedWithoutErrors => Errors.Count == 0;
}

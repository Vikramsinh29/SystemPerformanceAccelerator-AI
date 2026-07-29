namespace SystemPerformanceAccelerator.Core.Models;

public sealed record CustomCleanExecutionResult(
    int RequestedCount,
    int DeletedCount,
    int SkippedCount,
    int FailedCount,
    long ReclaimedBytes,
    IReadOnlyList<string> Errors,
    TimeSpan Elapsed)
{
    public bool CompletedWithoutIssues =>
        SkippedCount == 0 &&
        FailedCount == 0;
}

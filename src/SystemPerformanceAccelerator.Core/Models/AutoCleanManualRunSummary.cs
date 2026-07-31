namespace SystemPerformanceAccelerator.Core.Models;

public sealed record AutoCleanManualRunSummary(
    DateTime CompletedAtLocal,
    int RequestedCount,
    int DeletedCount,
    int SkippedCount,
    int FailedCount,
    long ReclaimedBytes,
    TimeSpan Elapsed,
    string FirstIssue)
{
    public const int MaximumFirstIssueLength = 500;

    public bool CompletedWithoutIssues =>
        SkippedCount == 0 &&
        FailedCount == 0;
}

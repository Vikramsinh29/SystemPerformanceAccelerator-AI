namespace SystemPerformanceAccelerator.Core.Models;

public sealed record CleanupResult(
    int DeletedCount,
    long ReclaimedBytes,
    IReadOnlyList<string> Errors,
    TimeSpan Elapsed)
{
    public bool CompletedWithoutErrors => Errors.Count == 0;
}

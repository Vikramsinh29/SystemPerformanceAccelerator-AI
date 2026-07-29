namespace SystemPerformanceAccelerator.Core.Models;

public sealed record ScanResult(
    IReadOnlyList<CleanupCandidate> Candidates,
    IReadOnlyList<string> Errors,
    TimeSpan Elapsed)
{
    public long TotalBytes => Candidates.Sum(candidate => candidate.SizeBytes);
}

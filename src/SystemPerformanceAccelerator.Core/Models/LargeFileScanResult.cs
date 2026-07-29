namespace SystemPerformanceAccelerator.Core.Models;

public sealed record LargeFileScanProgress(
    int FilesScanned,
    int DirectoriesScanned,
    string CurrentDirectory);

public sealed record LargeFileScanResult(
    IReadOnlyList<LargeFileCandidate> Candidates,
    IReadOnlyList<string> Errors,
    int FilesScanned,
    int DirectoriesScanned,
    TimeSpan Elapsed)
{
    public long TotalBytes => Candidates.Sum(candidate => candidate.SizeBytes);
}

namespace SystemPerformanceAccelerator.Core.Models;

public enum DuplicateFileScanPhase
{
    DiscoveringFiles,
    HashingCandidates
}

public sealed record DuplicateFileScanProgress(
    DuplicateFileScanPhase Phase,
    int FilesScanned,
    int DirectoriesScanned,
    int HashCandidatesProcessed,
    int HashCandidateCount,
    string CurrentPath);

public sealed record DuplicateFileScanResult(
    IReadOnlyList<DuplicateFileGroup> Groups,
    IReadOnlyList<string> Errors,
    int FilesScanned,
    int DirectoriesScanned,
    int FilesHashed,
    TimeSpan Elapsed)
{
    public int DuplicateFileCount => Groups.Sum(group => group.Files.Count);

    public long PotentialReclaimableBytes => Groups.Aggregate(
        0L,
        static (total, group) => SaturatingAdd(total, group.ReclaimableBytes));

    private static long SaturatingAdd(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;
}

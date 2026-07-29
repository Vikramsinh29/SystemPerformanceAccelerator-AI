namespace SystemPerformanceAccelerator.Core.Models;

public sealed record DuplicateFileGroup(
    string Sha256Hash,
    long SizeBytes,
    IReadOnlyList<DuplicateFileCandidate> Files)
{
    public long ReclaimableBytes => SaturatingMultiply(
        SizeBytes,
        Math.Max(0, Files.Count - 1));

    private static long SaturatingMultiply(long value, int multiplier)
    {
        if (value <= 0 || multiplier <= 0)
        {
            return 0;
        }

        return value > long.MaxValue / multiplier
            ? long.MaxValue
            : value * multiplier;
    }
}

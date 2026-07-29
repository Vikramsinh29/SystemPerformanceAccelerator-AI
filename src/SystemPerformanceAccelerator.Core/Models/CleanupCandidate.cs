namespace SystemPerformanceAccelerator.Core.Models;

public sealed record CleanupCandidate(
    string FullPath,
    long SizeBytes,
    DateTime LastWriteTimeUtc,
    bool IsSelected = true)
{
    public string Name => Path.GetFileName(FullPath);
}

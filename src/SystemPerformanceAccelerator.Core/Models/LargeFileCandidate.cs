namespace SystemPerformanceAccelerator.Core.Models;

public sealed record LargeFileCandidate(
    string FullPath,
    long SizeBytes,
    DateTime LastWriteTimeUtc)
{
    public string Name => Path.GetFileName(FullPath);
    public string Location => Path.GetDirectoryName(FullPath) ?? FullPath;
    public DateTime LastModified => LastWriteTimeUtc.ToLocalTime();
    public string SizeDisplay => FormatBytes(SizeBytes);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}

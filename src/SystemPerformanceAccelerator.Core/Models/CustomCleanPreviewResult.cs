namespace SystemPerformanceAccelerator.Core.Models;

public sealed record CustomCleanPreviewResult(
    IReadOnlyList<CustomCleanPreviewItem> Items,
    IReadOnlyList<string> Errors,
    TimeSpan Elapsed)
{
    public long TotalBytes => Items.Sum(item => item.SizeBytes);
}

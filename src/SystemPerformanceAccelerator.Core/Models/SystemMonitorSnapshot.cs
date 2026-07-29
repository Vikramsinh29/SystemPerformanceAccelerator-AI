namespace SystemPerformanceAccelerator.Core.Models;

public sealed record SystemMonitorSnapshot(
    DateTimeOffset CapturedAt,
    double CpuUsagePercent,
    long TotalPhysicalMemoryBytes,
    long AvailablePhysicalMemoryBytes)
{
    public long UsedPhysicalMemoryBytes =>
        Math.Max(0, TotalPhysicalMemoryBytes - AvailablePhysicalMemoryBytes);

    public double MemoryUsagePercent =>
        TotalPhysicalMemoryBytes <= 0
            ? 0
            : Math.Clamp(
                UsedPhysicalMemoryBytes * 100d / TotalPhysicalMemoryBytes,
                0,
                100);
}

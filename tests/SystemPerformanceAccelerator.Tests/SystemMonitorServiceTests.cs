using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class SystemMonitorServiceTests
{
    [Fact]
    public async Task CaptureAsync_ReturnsValidSystemSnapshot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new SystemMonitorService();

        var snapshot = await service.CaptureAsync(
            TimeSpan.FromMilliseconds(50));

        Assert.InRange(snapshot.CpuUsagePercent, 0, 100);
        Assert.True(snapshot.TotalPhysicalMemoryBytes > 0);
        Assert.InRange(
            snapshot.AvailablePhysicalMemoryBytes,
            0,
            snapshot.TotalPhysicalMemoryBytes);
        Assert.InRange(
            snapshot.UsedPhysicalMemoryBytes,
            0,
            snapshot.TotalPhysicalMemoryBytes);
        Assert.InRange(snapshot.MemoryUsagePercent, 0, 100);
    }

    [Fact]
    public async Task CaptureAsync_WithCancelledToken_StopsSafely()
    {
        var service = new SystemMonitorService();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CaptureAsync(
                TimeSpan.FromMilliseconds(50),
                cancellationTokenSource.Token));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11000)]
    public async Task CaptureAsync_WithInvalidSampleDuration_Throws(
        int milliseconds)
    {
        var service = new SystemMonitorService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.CaptureAsync(
                TimeSpan.FromMilliseconds(milliseconds)));
    }
}

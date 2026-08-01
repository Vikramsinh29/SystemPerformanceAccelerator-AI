using SystemPerformanceAccelerator.Desktop.ViewModels;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class SystemMonitorHistoryBufferTests
{
    [Fact]
    public void Add_RetainsNewestSamplesInChronologicalOrder()
    {
        var history = new SystemMonitorHistoryBuffer(capacity: 3);

        history.Add(10, 40);
        history.Add(20, 50);
        history.Add(30, 60);
        history.Add(40, 70);

        Assert.Equal(new[] { 20d, 30d, 40d }, history.CpuValues);
        Assert.Equal(new[] { 50d, 60d, 70d }, history.MemoryValues);
    }

    [Fact]
    public void Add_ClampsPercentagesToHonestChartRange()
    {
        var history = new SystemMonitorHistoryBuffer();

        history.Add(-5, 140);

        Assert.Equal(0d, Assert.Single(history.CpuValues));
        Assert.Equal(100d, Assert.Single(history.MemoryValues));
    }

    [Fact]
    public void Clear_RemovesCpuAndMemoryHistoryTogether()
    {
        var history = new SystemMonitorHistoryBuffer();
        history.Add(12, 34);

        history.Clear();

        Assert.Empty(history.CpuValues);
        Assert.Empty(history.MemoryValues);
        Assert.Equal(0, history.Count);
    }
}

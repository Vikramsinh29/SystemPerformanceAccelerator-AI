using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface ISystemMonitorService
{
    Task<SystemMonitorSnapshot> CaptureAsync(
        TimeSpan cpuSampleDuration,
        CancellationToken cancellationToken = default);
}

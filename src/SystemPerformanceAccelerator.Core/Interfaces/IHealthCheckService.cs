using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IHealthCheckService
{
    Task<HealthCheckResult> RunAsync(
        CancellationToken cancellationToken = default);
}

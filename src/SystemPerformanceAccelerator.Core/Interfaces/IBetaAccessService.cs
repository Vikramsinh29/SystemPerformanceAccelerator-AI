using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IBetaAccessService
{
    Task<BetaAccessStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<BetaAccessStatus> ActivateAsync(
        string accessCode,
        string applicationVersion,
        CancellationToken cancellationToken = default);
}

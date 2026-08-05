using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

[Obsolete(
    "Legacy controlled-beta access-code activation is not part of the production licensing runtime. Use ILicenseActivationService.")]
public interface IBetaAccessService
{
    Task<BetaAccessStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<BetaAccessStatus> ActivateAsync(
        string accessCode,
        string applicationVersion,
        CancellationToken cancellationToken = default);
}

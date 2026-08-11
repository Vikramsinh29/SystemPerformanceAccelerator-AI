using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface ILicenseActivationService
{
    Task<LicenseActivationResult> ActivateAsync(
        LicenseActivationRequest request,
        CancellationToken cancellationToken = default);

    Task<LicenseValidationResult> ValidateAsync(
        CancellationToken cancellationToken = default);

    Task<RemoteOperationResult> DeactivateAsync(
        CancellationToken cancellationToken = default);
}

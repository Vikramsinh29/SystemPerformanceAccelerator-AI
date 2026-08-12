using SystemPerformanceAccelerator.Core.Licensing;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IProductionLicensingClient
{
    Task<ProductionLicenseSnapshot?> GetAccountLicenseAsync(
        string bearerToken,
        CancellationToken cancellationToken = default);

    Task<ProductionLicensingMutationResult> ActivateDeviceAsync(
        string bearerToken,
        string deviceFingerprintHash,
        string? deviceLabel = null,
        CancellationToken cancellationToken = default);

    Task<ProductionLicensingMutationResult> DeactivateDeviceAsync(
        string bearerToken,
        string deviceFingerprintHash,
        CancellationToken cancellationToken = default);

    Task<ProductionDeviceValidationResult> ValidateDeviceAsync(
        string bearerToken,
        string deviceFingerprintHash,
        CancellationToken cancellationToken = default);
}

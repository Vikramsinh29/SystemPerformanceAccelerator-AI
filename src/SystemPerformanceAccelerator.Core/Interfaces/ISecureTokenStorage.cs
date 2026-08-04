namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface ISecureTokenStorage
{
    Task<string?> GetSessionTokenAsync(
        CancellationToken cancellationToken = default);

    Task StoreSessionTokenAsync(
        string sessionToken,
        CancellationToken cancellationToken = default);

    Task ClearSessionTokenAsync(
        CancellationToken cancellationToken = default);

    Task<string?> GetLicenseTokenAsync(
        CancellationToken cancellationToken = default);

    Task StoreLicenseTokenAsync(
        string licenseToken,
        CancellationToken cancellationToken = default);

    Task ClearLicenseTokenAsync(
        CancellationToken cancellationToken = default);
}

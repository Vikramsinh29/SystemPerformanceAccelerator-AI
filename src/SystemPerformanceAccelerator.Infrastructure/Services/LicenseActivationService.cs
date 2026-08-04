using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class LicenseActivationService : ILicenseActivationService
{
    private readonly DesktopApiClient _apiClient;
    private readonly ISecureTokenStorage _tokenStorage;
    private readonly IDeviceIdentityProvider _deviceIdentityProvider;

    public LicenseActivationService(
        DesktopApiClient apiClient,
        ISecureTokenStorage tokenStorage,
        IDeviceIdentityProvider deviceIdentityProvider)
    {
        _apiClient = apiClient ??
            throw new ArgumentNullException(nameof(apiClient));
        _tokenStorage = tokenStorage ??
            throw new ArgumentNullException(nameof(tokenStorage));
        _deviceIdentityProvider = deviceIdentityProvider ??
            throw new ArgumentNullException(nameof(deviceIdentityProvider));
    }

    public async Task<LicenseActivationResult> ActivateAsync(
        LicenseActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var deviceId = await _deviceIdentityProvider.GetDeviceIdAsync(
            cancellationToken);
        var sessionToken = await _tokenStorage.GetSessionTokenAsync(
            cancellationToken);
        var response = await _apiClient.SendAsync<ActivateApiRequest, ActivateApiResponse>(
            HttpMethod.Post,
            "api/licenses/activate",
            new ActivateApiRequest(request.ActivationKey, deviceId),
            bearerToken: sessionToken,
            allowRetry: false,
            cancellationToken);

        if (!response.Success)
        {
            return new LicenseActivationResult(
                false,
                null,
                null,
                response.Failure);
        }

        if (string.IsNullOrWhiteSpace(response.Payload?.LicenseToken))
        {
            return new LicenseActivationResult(
                false,
                null,
                null,
                new ApiFailure(
                    ApiErrorKind.UnexpectedResponse,
                    null,
                    null,
                    "The license activation response did not include a license token.",
                    false));
        }

        await _tokenStorage.StoreLicenseTokenAsync(
            response.Payload.LicenseToken,
            cancellationToken);
        return new LicenseActivationResult(
            true,
            response.Payload.LicenseToken,
            response.Payload.ToModel(),
            null);
    }

    public async Task<LicenseValidationResult> ValidateAsync(
        CancellationToken cancellationToken = default)
    {
        var licenseToken = await _tokenStorage.GetLicenseTokenAsync(
            cancellationToken);
        if (string.IsNullOrWhiteSpace(licenseToken))
        {
            return new LicenseValidationResult(
                false,
                null,
                new ApiFailure(
                    ApiErrorKind.ValidationFailed,
                    null,
                    "license_token_missing",
                    "No stored license token is available for validation.",
                    false));
        }

        var deviceId = await _deviceIdentityProvider.GetDeviceIdAsync(
            cancellationToken);
        var response = await _apiClient.SendAsync<ValidateApiRequest, ValidateApiResponse>(
            HttpMethod.Post,
            "api/licenses/validate",
            new ValidateApiRequest(deviceId),
            bearerToken: licenseToken,
            allowRetry: true,
            cancellationToken);

        return response.Success
            ? new LicenseValidationResult(
                true,
                response.Payload?.ToModel(),
                null)
            : new LicenseValidationResult(
                false,
                null,
                response.Failure);
    }

    public async Task<RemoteOperationResult> DeactivateAsync(
        CancellationToken cancellationToken = default)
    {
        var licenseToken = await _tokenStorage.GetLicenseTokenAsync(
            cancellationToken);
        if (string.IsNullOrWhiteSpace(licenseToken))
        {
            return new RemoteOperationResult(true, null);
        }

        var deviceId = await _deviceIdentityProvider.GetDeviceIdAsync(
            cancellationToken);
        var response = await _apiClient.SendAsync<DeactivateApiRequest, DeactivateApiResponse>(
            HttpMethod.Post,
            "api/licenses/deactivate",
            new DeactivateApiRequest(deviceId),
            bearerToken: licenseToken,
            allowRetry: false,
            cancellationToken);

        if (!response.Success)
        {
            return new RemoteOperationResult(false, response.Failure);
        }

        await _tokenStorage.ClearLicenseTokenAsync(cancellationToken);
        return new RemoteOperationResult(true, null);
    }

    internal sealed record ActivateApiRequest(
        string ActivationKey,
        string DeviceId);

    internal sealed record ActivateApiResponse(
        string? LicenseToken,
        string? LicenseId,
        string? Plan,
        string? Status,
        string? DeviceId,
        DateTimeOffset? ActivatedUtc,
        DateTimeOffset? ExpiresUtc,
        DateTimeOffset? ValidatedUtc)
    {
        public LicenseStatus ToModel() => new(
            LicenseId,
            Plan,
            Status,
            DeviceId,
            ActivatedUtc,
            ExpiresUtc,
            ValidatedUtc);
    }

    internal sealed record ValidateApiRequest(
        string DeviceId);

    internal sealed record ValidateApiResponse(
        string? LicenseId,
        string? Plan,
        string? Status,
        string? DeviceId,
        DateTimeOffset? ActivatedUtc,
        DateTimeOffset? ExpiresUtc,
        DateTimeOffset? ValidatedUtc,
        [property: JsonPropertyName("isValid")]
        bool IsValid)
    {
        public LicenseStatus ToModel() => new(
            LicenseId,
            Plan,
            Status ?? (IsValid ? "active" : "invalid"),
            DeviceId,
            ActivatedUtc,
            ExpiresUtc,
            ValidatedUtc);
    }

    internal sealed record DeactivateApiRequest(
        string DeviceId);

    internal sealed record DeactivateApiResponse(
        bool Success);
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

[Obsolete(
    "Legacy controlled-beta access-code activation is not part of the production licensing runtime. Use LicenseActivationService.")]
public sealed class BetaAccessService : IBetaAccessService
{
    public static readonly Uri ProductionEndpoint = new(
        "https://pc-spa-feedback-api.pc-spa-feedback.workers.dev/");

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly InstallationIdentityService _identityService;
    private readonly ICredentialProtector _credentialProtector;
    private readonly string _credentialPath;

    public BetaAccessService(
        InstallationIdentityService identityService,
        string credentialPath,
        HttpClient? httpClient = null,
        ICredentialProtector? credentialProtector = null)
    {
        ArgumentNullException.ThrowIfNull(identityService);
        if (string.IsNullOrWhiteSpace(credentialPath))
        {
            throw new ArgumentException(
                "A credential path is required.",
                nameof(credentialPath));
        }

        _identityService = identityService;
        _credentialPath = Path.GetFullPath(credentialPath);
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = ProductionEndpoint,
            Timeout = TimeSpan.FromSeconds(15)
        };
        _credentialProtector = credentialProtector ??
            new WindowsDataProtectionCredentialProtector();
    }

    public async Task<BetaAccessStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var credential = TryLoadCredential();
        if (credential is null)
        {
            return BetaAccessStatus.NotActivated;
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "v1/beta/verify",
                new
                {
                    entitlementToken = credential.EntitlementToken,
                    installationId = _identityService.GetOrCreate()
                },
                SerializerOptions,
                cancellationToken);

            var payload = await ReadPayloadAsync<VerificationResponse>(
                response,
                cancellationToken);
            if (payload is null)
            {
                return Unavailable(
                    "PC-SPA could not read the beta-access response.");
            }

            return new BetaAccessStatus(
                payload.Active,
                payload.Status ?? "not_valid",
                payload.EntitlementReference,
                payload.ActivatedUtc,
                payload.ExpiresUtc,
                payload.GracePeriodDays,
                payload.Active
                    ? "Controlled-beta access is active."
                    : "Controlled-beta access is no longer active.");
        }
        catch (Exception ex) when (IsConnectivityFailure(ex))
        {
            return CreateOfflineStatus(credential);
        }
    }

    public async Task<BetaAccessStatus> ActivateAsync(
        string accessCode,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessCode))
        {
            throw new ArgumentException(
                "A beta access code is required.",
                nameof(accessCode));
        }

        if (string.IsNullOrWhiteSpace(applicationVersion))
        {
            throw new ArgumentException(
                "An application version is required.",
                nameof(applicationVersion));
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "v1/beta/activate",
                new
                {
                    accessCode = accessCode.Trim(),
                    installationId = _identityService.GetOrCreate(),
                    applicationVersion
                },
                SerializerOptions,
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.Forbidden or
                HttpStatusCode.Conflict)
            {
                return new BetaAccessStatus(
                    false,
                    "activation_rejected",
                    null,
                    null,
                    null,
                    0,
                    "This access code is invalid, expired, already used, or has reached its activation limit.");
            }

            var payload = await ReadPayloadAsync<ActivationResponse>(
                response,
                cancellationToken);
            if (!response.IsSuccessStatusCode ||
                payload is null ||
                !payload.Activated ||
                string.IsNullOrWhiteSpace(payload.EntitlementToken))
            {
                return Unavailable(
                    "PC-SPA could not complete beta activation.");
            }

            SaveCredential(new StoredCredential(
                1,
                payload.EntitlementToken,
                payload.EntitlementReference,
                payload.ActivatedUtc,
                payload.ExpiresUtc));

            return new BetaAccessStatus(
                true,
                "active",
                payload.EntitlementReference,
                payload.ActivatedUtc,
                payload.ExpiresUtc,
                payload.GracePeriodDays,
                "Controlled-beta access was activated successfully.");
        }
        catch (Exception ex) when (IsConnectivityFailure(ex))
        {
            return Unavailable(
                "Activation could not reach the PC-SPA service. Check the internet connection and try again.");
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException or
            System.ComponentModel.Win32Exception or
            PlatformNotSupportedException)
        {
            return Unavailable(
                "Beta access was accepted, but PC-SPA could not securely store the entitlement on this Windows account. Try again or contact support.");
        }
    }

    private StoredCredential? TryLoadCredential()
    {
        try
        {
            if (!File.Exists(_credentialPath))
            {
                return null;
            }

            var protectedBytes = File.ReadAllBytes(_credentialPath);
            var plaintext = _credentialProtector.Unprotect(protectedBytes);
            try
            {
                return JsonSerializer.Deserialize<StoredCredential>(
                    plaintext,
                    SerializerOptions);
            }
            finally
            {
                Array.Clear(plaintext);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException or
            System.Security.SecurityException or
            System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private void SaveCredential(StoredCredential credential)
    {
        var directory = Path.GetDirectoryName(_credentialPath)
            ?? throw new InvalidOperationException(
                "The credential path has no parent directory.");
        Directory.CreateDirectory(directory);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
            credential,
            SerializerOptions);
        byte[]? protectedBytes = null;
        var temporaryPath = _credentialPath + ".tmp";
        try
        {
            protectedBytes = _credentialProtector.Protect(plaintext);
            File.WriteAllBytes(temporaryPath, protectedBytes);
            File.Move(temporaryPath, _credentialPath, overwrite: true);
        }
        finally
        {
            Array.Clear(plaintext);
            if (protectedBytes is not null)
            {
                Array.Clear(protectedBytes);
            }

            TryDelete(temporaryPath);
        }
    }

    private static async Task<T?> ReadPayloadAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(
                SerializerOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static bool IsConnectivityFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException;

    private static BetaAccessStatus Unavailable(string message) => new(
        false,
        "service_unavailable",
        null,
        null,
        null,
        0,
        message);

    private static BetaAccessStatus CreateOfflineStatus(
        StoredCredential credential)
    {
        var now = DateTimeOffset.UtcNow;
        if (credential.ExpiresUtc is not null &&
            credential.ExpiresUtc.Value > now)
        {
            return new BetaAccessStatus(
                true,
                "offline_grace",
                credential.EntitlementReference,
                credential.ActivatedUtc,
                credential.ExpiresUtc,
                0,
                "PC-SPA is using the securely stored beta entitlement because the verification service could not be reached. Online verification will be retried at the next launch.");
        }

        return Unavailable(
            "Beta access could not be verified and no unexpired local entitlement is available. Check the internet connection and try again.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
        }
    }

    private sealed record StoredCredential(
        int SchemaVersion,
        string EntitlementToken,
        string? EntitlementReference,
        DateTimeOffset? ActivatedUtc,
        DateTimeOffset? ExpiresUtc);

    private sealed record ActivationResponse(
        bool Activated,
        string? EntitlementReference,
        string? EntitlementToken,
        DateTimeOffset? ActivatedUtc,
        DateTimeOffset? ExpiresUtc,
        int AccessDays,
        int GracePeriodDays);

    private sealed record VerificationResponse(
        bool Active,
        string? Status,
        string? EntitlementReference,
        DateTimeOffset? ActivatedUtc,
        DateTimeOffset? ExpiresUtc,
        int GracePeriodDays);
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Licensing;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed partial class ProductionLicensingClient : IProductionLicensingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public ProductionLicensingClient(HttpClient httpClient, Uri baseUri)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));

        if (!_baseUri.IsAbsoluteUri || _baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Production licensing base URI must be an absolute HTTPS URI.", nameof(baseUri));
        }
    }

    public async Task<ProductionLicenseSnapshot?> GetAccountLicenseAsync(
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "account/license", bearerToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var envelope = await ReadJsonAsync<LicenseEnvelope>(response, cancellationToken).ConfigureAwait(false);
        return envelope.License ?? throw new HttpRequestException("Licensing response did not contain a license payload.");
    }

    public Task<ProductionLicensingMutationResult> ActivateDeviceAsync(
        string bearerToken,
        string deviceFingerprintHash,
        string? deviceLabel = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new DeviceMutationRequest(
            ValidateFingerprint(deviceFingerprintHash),
            string.IsNullOrWhiteSpace(deviceLabel) ? null : deviceLabel.Trim());

        return SendMutationAsync("activate", bearerToken, payload, cancellationToken);
    }

    public Task<ProductionLicensingMutationResult> DeactivateDeviceAsync(
        string bearerToken,
        string deviceFingerprintHash,
        CancellationToken cancellationToken = default)
    {
        var payload = new DeviceMutationRequest(ValidateFingerprint(deviceFingerprintHash), null);
        return SendMutationAsync("deactivate", bearerToken, payload, cancellationToken);
    }

    public async Task<ProductionDeviceValidationResult> ValidateDeviceAsync(
        string bearerToken,
        string deviceFingerprintHash,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "validate", bearerToken);
        request.Content = JsonContent.Create(
            new DeviceValidationRequest(ValidateFingerprint(deviceFingerprintHash)),
            options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var envelope = await ReadJsonAsync<ValidationEnvelope>(response, cancellationToken).ConfigureAwait(false);

        return new ProductionDeviceValidationResult(
            envelope.Valid,
            RequireCode(envelope.Code ?? envelope.Error),
            (int)response.StatusCode,
            envelope.License);
    }

    private async Task<ProductionLicensingMutationResult> SendMutationAsync(
        string relativePath,
        string bearerToken,
        DeviceMutationRequest payload,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, relativePath, bearerToken);
        request.Content = JsonContent.Create(payload, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var envelope = await ReadJsonAsync<MutationEnvelope>(response, cancellationToken).ConfigureAwait(false);

        return new ProductionLicensingMutationResult(
            response.IsSuccessStatusCode && envelope.Ok,
            RequireCode(envelope.Code ?? envelope.Error),
            (int)response.StatusCode,
            envelope.License);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, string bearerToken)
    {
        var token = ValidateBearerToken(bearerToken);
        var request = new HttpRequestMessage(method, new Uri(_baseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static string ValidateBearerToken(string bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            throw new ArgumentException("Bearer token is required.", nameof(bearerToken));
        }

        var token = bearerToken.Trim();
        if (token.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Bearer token must not contain whitespace.", nameof(bearerToken));
        }

        return token;
    }

    private static string ValidateFingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !FingerprintRegex().IsMatch(value))
        {
            throw new ArgumentException("Device fingerprint hash must be exactly 64 hexadecimal characters.", nameof(value));
        }

        return value.ToLowerInvariant();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await TryReadErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException(
            $"Licensing request failed with HTTP {(int)response.StatusCode} ({error ?? "unknown_error"}).",
            null,
            response.StatusCode);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return value ?? throw new HttpRequestException("Licensing response body was empty.");
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("Licensing response contained invalid JSON.", exception, response.StatusCode);
        }
    }

    private static async Task<string?> TryReadErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return envelope?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string RequireCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code) ? "licensing_failure" : code;
    }

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintRegex();

    private sealed record DeviceMutationRequest(string DeviceFingerprintHash, string? DeviceLabel);
    private sealed record DeviceValidationRequest(string DeviceFingerprintHash);
    private sealed record LicenseEnvelope(ProductionLicenseSnapshot? License);
    private sealed record MutationEnvelope(bool Ok, string? Code, string? Error, ProductionLicenseSnapshot? License);
    private sealed record ValidationEnvelope(bool Valid, string? Code, string? Error, ProductionLicenseSnapshot? License);
    private sealed record ErrorEnvelope(string? Error);
}

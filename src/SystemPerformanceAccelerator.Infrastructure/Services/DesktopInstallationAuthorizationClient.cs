using System.Net.Http.Json;
using System.Text.Json;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class DesktopInstallationAuthorizationClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Uri _exchangeUri;

    public DesktopInstallationAuthorizationClient(
        HttpClient httpClient,
        Uri exchangeUri)
    {
        _httpClient =
            httpClient ??
            throw new ArgumentNullException(
                nameof(httpClient));

        _exchangeUri =
            exchangeUri ??
            throw new ArgumentNullException(
                nameof(exchangeUri));

        if (
            !_exchangeUri.IsAbsoluteUri ||
            _exchangeUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "Authorization exchange URI must use HTTPS.",
                nameof(exchangeUri));
        }
    }

    public async Task<InstallationAuthorizationResult>
        ExchangeAsync(
            string authorizationCode,
            CancellationToken cancellationToken = default)
    {
        var code =
            ValidateAuthorizationCode(
                authorizationCode);

        using var response =
            await _httpClient.PostAsJsonAsync(
                _exchangeUri,
                new ExchangeRequest(code),
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new InstallationAuthorizationResult(
                false,
                null,
                null,
                "authorization_exchange_failed");
        }

        ExchangeResponse? payload;

        try
        {
            payload =
                await response.Content
                    .ReadFromJsonAsync<ExchangeResponse>(
                        JsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return new InstallationAuthorizationResult(
                false,
                null,
                null,
                "invalid_authorization_response");
        }

        if (payload is null)
        {
            return new InstallationAuthorizationResult(
                false,
                null,
                null,
                "invalid_authorization_response");
        }

        string token;

        try
        {
            token =
                ValidateReturnedToken(
                    payload.Token);
        }
        catch (InvalidOperationException)
        {
            return new InstallationAuthorizationResult(
                false,
                null,
                null,
                "invalid_authorization_response");
        }

        if (
            !string.Equals(
                payload.TokenType,
                "Bearer",
                StringComparison.OrdinalIgnoreCase))
        {
            return new InstallationAuthorizationResult(
                false,
                null,
                null,
                "invalid_authorization_response");
        }

        if (
            payload.ExpiresInSeconds < 60 ||
            payload.ExpiresInSeconds > 86400)
        {
            return new InstallationAuthorizationResult(
                false,
                null,
                null,
                "invalid_authorization_response");
        }

        var expiresUtc =
            DateTimeOffset.UtcNow.AddSeconds(
                payload.ExpiresInSeconds);

        return new InstallationAuthorizationResult(
            true,
            token,
            expiresUtc,
            "authorized");
    }

    private static string ValidateAuthorizationCode(
        string authorizationCode)
    {
        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            throw new ArgumentException(
                "Authorization code is required.",
                nameof(authorizationCode));
        }

        var code =
            authorizationCode.Trim();

        if (
            code.Length > 1024 ||
            code.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Authorization code is invalid.",
                nameof(authorizationCode));
        }

        return code;
    }

    private static string ValidateReturnedToken(
        string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Authorization response did not contain a token.");
        }

        var value =
            token.Trim();

        if (
            value.Length > 4096 ||
            value.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException(
                "Authorization response contained an invalid token.");
        }

        return value;
    }

    private sealed record ExchangeRequest(
        string AuthorizationCode);

    private sealed record ExchangeResponse(
        string? Token,
        string? TokenType,
        int ExpiresInSeconds);
}

public sealed record InstallationAuthorizationResult(
    bool Success,
    string? BearerToken,
    DateTimeOffset? ExpiresUtc,
    string Code);
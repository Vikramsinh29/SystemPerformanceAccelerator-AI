using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly DesktopApiClient _apiClient;
    private readonly ISecureTokenStorage _tokenStorage;

    public AuthenticationService(
        DesktopApiClient apiClient,
        ISecureTokenStorage tokenStorage)
    {
        _apiClient = apiClient ??
            throw new ArgumentNullException(nameof(apiClient));
        _tokenStorage = tokenStorage ??
            throw new ArgumentNullException(nameof(tokenStorage));
    }

    public async Task<AuthLoginResult> LoginAsync(
        AuthLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _apiClient.SendAsync<LoginApiRequest, LoginApiResponse>(
            HttpMethod.Post,
            "api/auth/login",
            new LoginApiRequest(request.Email, request.Password),
            bearerToken: null,
            allowRetry: false,
            cancellationToken);

        if (!response.Success)
        {
            return new AuthLoginResult(false, null, null, response.Failure);
        }

        if (string.IsNullOrWhiteSpace(response.Payload?.SessionToken))
        {
            return new AuthLoginResult(
                false,
                null,
                null,
                new ApiFailure(
                    ApiErrorKind.UnexpectedResponse,
                    null,
                    null,
                    "The authentication response did not include a session token.",
                    false));
        }

        await _tokenStorage.StoreSessionTokenAsync(
            response.Payload.SessionToken,
            cancellationToken);
        return new AuthLoginResult(
            true,
            response.Payload.SessionToken,
            response.Payload.ToModel(),
            null);
    }

    public async Task<RemoteOperationResult> LogoutAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await _tokenStorage.GetSessionTokenAsync(
            cancellationToken);
        var response = await _apiClient.SendAsync<object, LogoutApiResponse>(
            HttpMethod.Post,
            "api/auth/logout",
            request: null,
            bearerToken: token,
            allowRetry: false,
            cancellationToken);

        if (!response.Success)
        {
            return new RemoteOperationResult(false, response.Failure);
        }

        await _tokenStorage.ClearSessionTokenAsync(cancellationToken);
        return new RemoteOperationResult(true, null);
    }

    public async Task<AuthSessionResult> GetSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await _tokenStorage.GetSessionTokenAsync(
            cancellationToken);
        var response = await _apiClient.SendAsync<object, SessionApiResponse>(
            HttpMethod.Get,
            "api/auth/session",
            request: null,
            bearerToken: token,
            allowRetry: true,
            cancellationToken);

        return response.Success
            ? new AuthSessionResult(true, response.Payload?.ToModel(), null)
            : new AuthSessionResult(false, null, response.Failure);
    }

    internal sealed record LoginApiRequest(
        string Email,
        string Password);

    internal sealed record LoginApiResponse(
        string? SessionToken,
        string? UserId,
        string? Email,
        string? DisplayName,
        bool Authenticated,
        DateTimeOffset? ExpiresUtc)
    {
        public AuthSession ToModel() => new(
            UserId,
            Email,
            DisplayName,
            Authenticated,
            ExpiresUtc);
    }

    internal sealed record LogoutApiResponse(
        bool Success);

    internal sealed record SessionApiResponse(
        string? UserId,
        string? Email,
        string? DisplayName,
        [property: JsonPropertyName("isAuthenticated")]
        bool IsAuthenticated,
        DateTimeOffset? ExpiresUtc)
    {
        public AuthSession ToModel() => new(
            UserId,
            Email,
            DisplayName,
            IsAuthenticated,
            ExpiresUtc);
    }
}

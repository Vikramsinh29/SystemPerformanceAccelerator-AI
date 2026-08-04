using System.Net;
using System.Text;
using System.Text.Json;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class AuthenticationServiceFoundationTests
{
    [Fact]
    public async Task LoginAsync_SerializesRequest_ParsesResponse_AndStoresToken()
    {
        using var location = new TemporaryLocation();
        var handler = new RecordingHandler(request =>
            JsonResponse(HttpStatusCode.OK, new
            {
                sessionToken = "session-token-123",
                userId = "user-1",
                email = "user@example.com",
                displayName = "PC SPA User",
                authenticated = true,
                expiresUtc = "2026-08-30T12:00:00Z"
            }));
        var tokenStorage = new FileSecureTokenStorage(
            location.TokenPath,
            new PrefixCredentialProtector());
        var service = new AuthenticationService(
            new DesktopApiClient(
                new HttpClient(handler)
                {
                    BaseAddress = new Uri("https://desktop.test/")
                },
                TimeSpan.FromSeconds(2)),
            tokenStorage);

        var result = await service.LoginAsync(
            new AuthLoginRequest("user@example.com", "password-123"));

        Assert.True(result.Success);
        Assert.Equal("session-token-123", result.SessionToken);
        Assert.Equal("user@example.com", result.Session?.Email);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("user@example.com", json.RootElement.GetProperty("email").GetString());
        Assert.Equal("password-123", json.RootElement.GetProperty("password").GetString());
        Assert.Equal("session-token-123", await tokenStorage.GetSessionTokenAsync());
        Assert.DoesNotContain("session-token-123", File.ReadAllText(location.TokenPath));
    }

    [Fact]
    public async Task GetSessionAsync_RetriesSafeTransientFailure()
    {
        var attempts = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent(
                        "{\"code\":\"temporarily_unavailable\",\"message\":\"Try later.\"}",
                        Encoding.UTF8,
                        "application/json")
                }
                : JsonResponse(HttpStatusCode.OK, new
                {
                    userId = "user-2",
                    email = "stable@example.com",
                    displayName = "Stable User",
                    isAuthenticated = true,
                    expiresUtc = "2026-08-30T12:00:00Z"
                });
        });
        var tokenStorage = new InMemorySecureTokenStorage
        {
            SessionToken = "persisted-session"
        };
        var service = new AuthenticationService(
            new DesktopApiClient(
                new HttpClient(handler)
                {
                    BaseAddress = new Uri("https://desktop.test/")
                },
                TimeSpan.FromSeconds(2)),
            tokenStorage);

        var result = await service.GetSessionAsync();

        Assert.True(result.Success);
        Assert.Equal(2, attempts);
        Assert.Equal("stable@example.com", result.Session?.Email);
        Assert.Equal("Bearer persisted-session", handler.AuthorizationHeader);
    }

    [Fact]
    public async Task LoginAsync_MapsUnauthorizedWithoutRetry()
    {
        var attempts = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    "{\"code\":\"invalid_credentials\",\"message\":\"Invalid credentials.\"}",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var service = new AuthenticationService(
            new DesktopApiClient(
                new HttpClient(handler)
                {
                    BaseAddress = new Uri("https://desktop.test/")
                },
                TimeSpan.FromSeconds(2)),
            new InMemorySecureTokenStorage());

        var result = await service.LoginAsync(
            new AuthLoginRequest("user@example.com", "wrong-password"));

        Assert.False(result.Success);
        Assert.Equal(ApiErrorKind.AuthenticationFailed, result.Failure?.Kind);
        Assert.Equal("invalid_credentials", result.Failure?.Code);
        Assert.Equal(1, attempts);
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        object value) => new(statusCode)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(value),
            Encoding.UTF8,
            "application/json")
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) :
        HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        public string? AuthorizationHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            return responder(request);
        }
    }

    private sealed class InMemorySecureTokenStorage : Core.Interfaces.ISecureTokenStorage
    {
        public string? SessionToken { get; set; }

        public string? LicenseToken { get; set; }

        public Task<string?> GetSessionTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SessionToken);

        public Task StoreSessionTokenAsync(
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            SessionToken = sessionToken;
            return Task.CompletedTask;
        }

        public Task ClearSessionTokenAsync(
            CancellationToken cancellationToken = default)
        {
            SessionToken = null;
            return Task.CompletedTask;
        }

        public Task<string?> GetLicenseTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LicenseToken);

        public Task StoreLicenseTokenAsync(
            string licenseToken,
            CancellationToken cancellationToken = default)
        {
            LicenseToken = licenseToken;
            return Task.CompletedTask;
        }

        public Task ClearLicenseTokenAsync(
            CancellationToken cancellationToken = default)
        {
            LicenseToken = null;
            return Task.CompletedTask;
        }
    }

    private sealed class PrefixCredentialProtector : ICredentialProtector
    {
        private static readonly byte[] Prefix = "protected:"u8.ToArray();
        private const byte Mask = 0x5A;

        public byte[] Protect(byte[] plaintext)
        {
            var protectedBytes = new byte[Prefix.Length + plaintext.Length];
            Prefix.CopyTo(protectedBytes, 0);
            for (var index = 0; index < plaintext.Length; index++)
            {
                protectedBytes[Prefix.Length + index] =
                    (byte)(plaintext[index] ^ Mask);
            }

            return protectedBytes;
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            var plaintext = protectedData.AsSpan(Prefix.Length).ToArray();
            for (var index = 0; index < plaintext.Length; index++)
            {
                plaintext[index] = (byte)(plaintext[index] ^ Mask);
            }

            return plaintext;
        }
    }

    private sealed class TemporaryLocation : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"pc-spa-auth-foundation-tests-{Guid.NewGuid():N}");

        public string TokenPath => Path.Combine(Root, "tokens.dat");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

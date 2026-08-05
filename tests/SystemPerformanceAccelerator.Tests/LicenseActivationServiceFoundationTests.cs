using System.Net;
using System.Text;
using System.Text.Json;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class LicenseActivationServiceFoundationTests
{
    [Fact]
    public async Task ActivateAsync_SerializesActivationRequest_AndStoresOnlyIssuedToken()
    {
        using var location = new TemporaryLocation();
        var handler = new RecordingHandler(request =>
            JsonResponse(HttpStatusCode.OK, new
            {
                licenseToken = "license-token-456",
                licenseId = "lic-1",
                plan = "pro",
                status = "active",
                deviceId = "device-abc",
                activatedUtc = "2026-08-04T12:00:00Z",
                expiresUtc = "2026-09-04T12:00:00Z",
                validatedUtc = "2026-08-04T12:00:00Z"
            }));
        var tokenStorage = new FileSecureTokenStorage(
            location.TokenPath,
            new PrefixCredentialProtector());
        await tokenStorage.StoreSessionTokenAsync("session-token-123");
        var service = new LicenseActivationService(
            new DesktopApiClient(
                new HttpClient(handler)
                {
                    BaseAddress = new Uri("https://desktop.test/")
                },
                TimeSpan.FromSeconds(2)),
            tokenStorage,
            new StableDeviceIdentityProvider("device-abc"));

        var result = await service.ActivateAsync(
            new LicenseActivationRequest("  D1-Key.MixedCase-123  "));

        Assert.True(result.Success);
        Assert.Equal("license-token-456", result.LicenseToken);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/licenses/activate", handler.Path);
        Assert.Equal("Bearer session-token-123", handler.AuthorizationHeader);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("D1-Key.MixedCase-123", json.RootElement.GetProperty("activationKey").GetString());
        Assert.Equal("device-abc", json.RootElement.GetProperty("deviceId").GetString());
        Assert.Equal("license-token-456", await tokenStorage.GetLicenseTokenAsync());
        Assert.DoesNotContain("D1-Key.MixedCase-123", File.ReadAllText(location.TokenPath));
    }

    [Fact]
    public async Task ValidateAsync_UsesStoredToken_AndRetriesTransientFailure()
    {
        var attempts = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent(
                        "{\"code\":\"rate_limited\",\"message\":\"Slow down.\"}",
                        Encoding.UTF8,
                        "application/json")
                }
                : JsonResponse(HttpStatusCode.OK, new
                {
                    licenseId = "lic-2",
                    plan = "standard",
                    status = "active",
                    deviceId = "device-xyz",
                    activatedUtc = "2026-08-04T12:00:00Z",
                    expiresUtc = "2026-09-04T12:00:00Z",
                    validatedUtc = "2026-08-05T12:00:00Z",
                    isValid = true
                });
        });
        var tokenStorage = new InMemorySecureTokenStorage
        {
            LicenseToken = "stored-license-token"
        };
        var service = new LicenseActivationService(
            new DesktopApiClient(
                new HttpClient(handler)
                {
                    BaseAddress = new Uri("https://desktop.test/")
                },
                TimeSpan.FromSeconds(2)),
            tokenStorage,
            new StableDeviceIdentityProvider("device-xyz"));

        var result = await service.ValidateAsync();

        Assert.True(result.Success);
        Assert.Equal(2, attempts);
        Assert.Equal("active", result.License?.Status);
        Assert.Equal("/api/licenses/validate", handler.Path);
        Assert.Equal("Bearer stored-license-token", handler.AuthorizationHeader);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("device-xyz", json.RootElement.GetProperty("deviceId").GetString());
    }

    [Fact]
    public async Task ValidateAsync_WhenNoStoredToken_FailsClosed()
    {
        var service = new LicenseActivationService(
            new DesktopApiClient(
                new HttpClient(new RecordingHandler(_ =>
                    throw new InvalidOperationException("HTTP should not be called.")))
                {
                    BaseAddress = new Uri("https://desktop.test/")
                },
                TimeSpan.FromSeconds(2)),
            new InMemorySecureTokenStorage(),
            new StableDeviceIdentityProvider("device-xyz"));

        var result = await service.ValidateAsync();

        Assert.False(result.Success);
        Assert.Equal(ApiErrorKind.ValidationFailed, result.Failure?.Kind);
    }

    [Fact]
    public async Task DeactivateAsync_UsesStoredLicenseToken_AndCorrectContract()
    {
        var handler = new RecordingHandler(_ =>
            JsonResponse(HttpStatusCode.OK, new
            {
                success = true
            }));
        var tokenStorage = new InMemorySecureTokenStorage
        {
            LicenseToken = "stored-license-token"
        };
        var service = new LicenseActivationService(
            new DesktopApiClient(
                new HttpClient(handler)
                {
                    BaseAddress = new Uri("https://desktop.test/")
                },
                TimeSpan.FromSeconds(2)),
            tokenStorage,
            new StableDeviceIdentityProvider("device-deactivate"));

        var result = await service.DeactivateAsync();

        Assert.True(result.Success);
        Assert.Null(await tokenStorage.GetLicenseTokenAsync());
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/licenses/deactivate", handler.Path);
        Assert.Equal("Bearer stored-license-token", handler.AuthorizationHeader);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("device-deactivate", json.RootElement.GetProperty("deviceId").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "INVALID_KEY", ApiErrorKind.InvalidRequest)]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", ApiErrorKind.AuthenticationFailed)]
    [InlineData(HttpStatusCode.Forbidden, "WRONG_USER", ApiErrorKind.AuthorizationFailed)]
    [InlineData(HttpStatusCode.Conflict, "PENDING", ApiErrorKind.Conflict)]
    [InlineData(HttpStatusCode.Conflict, "REVOKED", ApiErrorKind.Conflict)]
    [InlineData(HttpStatusCode.Conflict, "EXPIRED", ApiErrorKind.Conflict)]
    [InlineData(HttpStatusCode.Conflict, "ACTIVATION_LIMIT", ApiErrorKind.Conflict)]
    [InlineData(HttpStatusCode.Conflict, "DEVICE_ALREADY_ACTIVE", ApiErrorKind.Conflict)]
    public async Task ActivateAsync_PreservesBackendDiagnosticCodes(
        HttpStatusCode statusCode,
        string code,
        ApiErrorKind expectedKind)
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        code,
                        message = "Rejected."
                    }),
                    Encoding.UTF8,
                    "application/json")
            });
        var tokenStorage = new InMemorySecureTokenStorage
        {
            SessionToken = "session-token"
        };
        var service = new LicenseActivationService(
            new DesktopApiClient(
                new HttpClient(handler)
                {
                    BaseAddress = new Uri("https://desktop.test/")
                },
                TimeSpan.FromSeconds(2)),
            tokenStorage,
            new StableDeviceIdentityProvider("device-abc"));

        var result = await service.ActivateAsync(
            new LicenseActivationRequest("d1-issued-key"));

        Assert.False(result.Success);
        Assert.Equal(expectedKind, result.Failure?.Kind);
        Assert.Equal(code, result.Failure?.Code);
        Assert.Equal("Bearer session-token", handler.AuthorizationHeader);
    }

    [Fact]
    public async Task RestartValidation_SucceedsWithStoredDpapiProtectedLicenseToken()
    {
        using var location = new TemporaryLocation();
        var activationHandler = new RecordingHandler(_ =>
            JsonResponse(HttpStatusCode.OK, new
            {
                licenseToken = "license-token-after-restart",
                licenseId = "lic-restart",
                plan = "pro",
                status = "active",
                deviceId = "device-restart",
                activatedUtc = "2026-08-04T12:00:00Z",
                validatedUtc = "2026-08-04T12:00:00Z"
            }));
        var tokenStorage = new FileSecureTokenStorage(
            location.TokenPath,
            new PrefixCredentialProtector());
        await tokenStorage.StoreSessionTokenAsync("session-token");
        var activationService = new LicenseActivationService(
            new DesktopApiClient(
                new HttpClient(activationHandler)
                {
                    BaseAddress = new Uri("https://desktop.test/")
                },
                TimeSpan.FromSeconds(2)),
            tokenStorage,
            new StableDeviceIdentityProvider("device-restart"));

        var activationResult = await activationService.ActivateAsync(
            new LicenseActivationRequest("restart-key"));

        Assert.True(activationResult.Success);
        Assert.DoesNotContain("license-token-after-restart", File.ReadAllText(location.TokenPath));

        var validationHandler = new RecordingHandler(_ =>
            JsonResponse(HttpStatusCode.OK, new
            {
                licenseId = "lic-restart",
                plan = "pro",
                status = "active",
                deviceId = "device-restart",
                activatedUtc = "2026-08-04T12:00:00Z",
                validatedUtc = "2026-08-05T12:00:00Z",
                isValid = true
            }));
        var restartedStorage = new FileSecureTokenStorage(
            location.TokenPath,
            new PrefixCredentialProtector());
        var restartedService = new LicenseActivationService(
            new DesktopApiClient(
                new HttpClient(validationHandler)
                {
                    BaseAddress = new Uri("https://desktop.test/")
                },
                TimeSpan.FromSeconds(2)),
            restartedStorage,
            new StableDeviceIdentityProvider("device-restart"));

        var validationResult = await restartedService.ValidateAsync();

        Assert.True(validationResult.Success);
        Assert.Equal("active", validationResult.License?.Status);
        Assert.Equal("Bearer license-token-after-restart", validationHandler.AuthorizationHeader);
    }

    [Fact]
    public async Task LicenseRuntime_DoesNotCallLegacyBetaAccessEndpoint()
    {
        var handler = new RecordingHandler(_ =>
            JsonResponse(HttpStatusCode.OK, new
            {
                licenseToken = "license-token",
                status = "active",
                deviceId = "device-abc"
            }));
        var tokenStorage = new InMemorySecureTokenStorage
        {
            SessionToken = "session-token"
        };
        var service = new LicenseActivationService(
            new DesktopApiClient(
                new HttpClient(handler)
                {
                    BaseAddress = new Uri("https://desktop.test/")
                },
                TimeSpan.FromSeconds(2)),
            tokenStorage,
            new StableDeviceIdentityProvider("device-abc"));

        await service.ActivateAsync(new LicenseActivationRequest("PCSPA-BETA-LEGACY-SHAPE"));

        Assert.Equal("/api/licenses/activate", handler.Path);
        Assert.DoesNotContain("/v1/beta", handler.Path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessCode", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("installationId", handler.RequestBody, StringComparison.Ordinal);
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

        public HttpMethod? Method { get; private set; }

        public string? Path { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            return responder(request);
        }
    }

    private sealed class StableDeviceIdentityProvider(string deviceId) :
        Core.Interfaces.IDeviceIdentityProvider
    {
        public Task<string> GetDeviceIdAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(deviceId);
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
            $"pc-spa-license-foundation-tests-{Guid.NewGuid():N}");

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

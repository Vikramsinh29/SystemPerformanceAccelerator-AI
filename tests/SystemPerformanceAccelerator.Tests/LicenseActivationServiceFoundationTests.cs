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
            new LicenseActivationRequest("ACTIVATION-KEY-123"));

        Assert.True(result.Success);
        Assert.Equal("license-token-456", result.LicenseToken);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("ACTIVATION-KEY-123", json.RootElement.GetProperty("activationKey").GetString());
        Assert.Equal("device-abc", json.RootElement.GetProperty("deviceId").GetString());
        Assert.Equal("license-token-456", await tokenStorage.GetLicenseTokenAsync());
        Assert.DoesNotContain("ACTIVATION-KEY-123", File.ReadAllText(location.TokenPath));
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
        Assert.Equal("Bearer stored-license-token", handler.AuthorizationHeader);
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

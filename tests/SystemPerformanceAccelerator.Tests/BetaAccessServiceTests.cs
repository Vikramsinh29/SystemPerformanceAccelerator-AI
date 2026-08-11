using System.Net;
using System.Text;
using System.Text.Json;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

#pragma warning disable CS0618 // Legacy service is covered only by migration/regression tests.
public sealed class BetaAccessServiceTests
{
    [Fact]
    public async Task ActivateAsync_CapturesAndProtectsReturnedCredential()
    {
        using var location = new TemporaryLocation();
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("/v1/beta/activate", request.RequestUri?.AbsolutePath);
            return JsonResponse(HttpStatusCode.Created, new
            {
                activated = true,
                entitlementReference = "ENT-0123456789ABCDEF",
                entitlementToken = new string('a', 64),
                activatedUtc = "2026-08-03T05:23:34.866Z",
                expiresUtc = "2026-09-02T05:23:34.866Z",
                accessDays = 30,
                gracePeriodDays = 0
            });
        });
        var protector = new PrefixCredentialProtector();
        var service = CreateService(location, handler, protector);

        var result = await service.ActivateAsync(
            "PCSPA-0123456789ABCDEF",
            "1.0.0-beta.1");

        Assert.True(result.IsActive);
        Assert.Equal("ENT-0123456789ABCDEF", result.EntitlementReference);
        Assert.True(File.Exists(location.CredentialPath));
        var persisted = File.ReadAllBytes(location.CredentialPath);
        Assert.StartsWith("protected:", Encoding.UTF8.GetString(persisted));
        Assert.DoesNotContain(new string('a', 64), Encoding.UTF8.GetString(persisted));
    }

    [Fact]
    public async Task GetStatusAsync_UsesStoredTokenAndStableInstallationId()
    {
        using var location = new TemporaryLocation();
        var requests = new List<string>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            requests.Add(await request.Content!.ReadAsStringAsync());
            if (request.RequestUri?.AbsolutePath == "/v1/beta/activate")
            {
                return JsonResponse(HttpStatusCode.Created, new
                {
                    activated = true,
                    entitlementReference = "ENT-0123456789ABCDEF",
                    entitlementToken = new string('b', 64),
                    activatedUtc = "2026-08-03T05:23:34.866Z",
                    expiresUtc = "2026-09-02T05:23:34.866Z",
                    accessDays = 30,
                    gracePeriodDays = 0
                });
            }

            return JsonResponse(HttpStatusCode.OK, new
            {
                active = true,
                status = "active",
                entitlementReference = "ENT-0123456789ABCDEF",
                activatedUtc = "2026-08-03T05:23:34.866Z",
                expiresUtc = "2026-09-02T05:23:34.866Z",
                gracePeriodDays = 0
            });
        });
        var service = CreateService(
            location,
            handler,
            new PrefixCredentialProtector());

        await service.ActivateAsync("PCSPA-0123456789ABCDEF", "1.0.0");
        var status = await service.GetStatusAsync();

        Assert.True(status.IsActive);
        Assert.Equal("active", status.Status);
        Assert.Equal(2, requests.Count);
        using var activation = JsonDocument.Parse(requests[0]);
        using var verification = JsonDocument.Parse(requests[1]);
        Assert.Equal(
            activation.RootElement.GetProperty("installationId").GetString(),
            verification.RootElement.GetProperty("installationId").GetString());
        Assert.Equal(
            new string('b', 64),
            verification.RootElement.GetProperty("entitlementToken").GetString());
    }

    [Fact]
    public async Task GetStatusAsync_WhenNoCredential_ReturnsNotActivated()
    {
        using var location = new TemporaryLocation();
        var handler = new StubHttpMessageHandler(_ =>
            Task.FromException<HttpResponseMessage>(
                new InvalidOperationException("HTTP must not be called.")));
        var service = CreateService(
            location,
            handler,
            new PrefixCredentialProtector());

        var result = await service.GetStatusAsync();

        Assert.False(result.IsActive);
        Assert.Equal("not_activated", result.Status);
    }

    [Fact]
    public async Task GetStatusAsync_WhenOfflineAndCredentialUnexpired_AllowsOfflineUse()
    {
        using var location = new TemporaryLocation();
        var onlineHandler = new StubHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.Created, new
            {
                activated = true,
                entitlementReference = "ENT-OFFLINE",
                entitlementToken = new string('c', 64),
                activatedUtc = DateTimeOffset.UtcNow.AddDays(-1),
                expiresUtc = DateTimeOffset.UtcNow.AddDays(2),
                accessDays = 3,
                gracePeriodDays = 0
            }));
        var protector = new PrefixCredentialProtector();
        var activationService = CreateService(location, onlineHandler, protector);
        await activationService.ActivateAsync("PCSPA-OFFLINE", "1.0.0");

        var offlineService = CreateService(
            location,
            new StubHttpMessageHandler(_ =>
                Task.FromException<HttpResponseMessage>(
                    new HttpRequestException("offline"))),
            protector);

        var result = await offlineService.GetStatusAsync();

        Assert.True(result.IsActive);
        Assert.Equal("offline_grace", result.Status);
        Assert.Equal("ENT-OFFLINE", result.EntitlementReference);
    }

    [Fact]
    public async Task GetStatusAsync_WhenOfflineAndCredentialExpired_DeniesAccess()
    {
        using var location = new TemporaryLocation();
        var onlineHandler = new StubHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.Created, new
            {
                activated = true,
                entitlementReference = "ENT-EXPIRED",
                entitlementToken = new string('d', 64),
                activatedUtc = DateTimeOffset.UtcNow.AddDays(-3),
                expiresUtc = DateTimeOffset.UtcNow.AddDays(-1),
                accessDays = 2,
                gracePeriodDays = 0
            }));
        var protector = new PrefixCredentialProtector();
        var activationService = CreateService(location, onlineHandler, protector);
        await activationService.ActivateAsync("PCSPA-EXPIRED", "1.0.0");

        var offlineService = CreateService(
            location,
            new StubHttpMessageHandler(_ =>
                Task.FromException<HttpResponseMessage>(
                    new HttpRequestException("offline"))),
            protector);

        var result = await offlineService.GetStatusAsync();

        Assert.False(result.IsActive);
        Assert.Equal("service_unavailable", result.Status);
    }

    private static BetaAccessService CreateService(
        TemporaryLocation location,
        HttpMessageHandler handler,
        ICredentialProtector protector)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = BetaAccessService.ProductionEndpoint
        };
        return new BetaAccessService(
            new InstallationIdentityService(location.IdentityPath),
            location.CredentialPath,
            client,
            protector);
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

    private sealed class PrefixCredentialProtector : ICredentialProtector
    {
        private static readonly byte[] Prefix = "protected:"u8.ToArray();
        private const byte TestMask = 0xA5;

        public byte[] Protect(byte[] plaintext)
        {
            var protectedData = new byte[Prefix.Length + plaintext.Length];
            Prefix.CopyTo(protectedData, 0);

            for (var index = 0; index < plaintext.Length; index++)
            {
                protectedData[Prefix.Length + index] =
                    (byte)(plaintext[index] ^ TestMask);
            }

            return protectedData;
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            if (protectedData.Length < Prefix.Length ||
                !protectedData.AsSpan(0, Prefix.Length).SequenceEqual(Prefix))
            {
                throw new InvalidOperationException(
                    "The test credential is invalid.");
            }

            var plaintext = new byte[protectedData.Length - Prefix.Length];
            for (var index = 0; index < plaintext.Length; index++)
            {
                plaintext[index] =
                    (byte)(protectedData[Prefix.Length + index] ^ TestMask);
            }

            return plaintext;
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>>
            _handler;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }

    private sealed class TemporaryLocation : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"pc-spa-beta-access-tests-{Guid.NewGuid():N}");

        public string IdentityPath => Path.Combine(Root, "installation.json");

        public string CredentialPath => Path.Combine(Root, "beta-access.dat");

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
#pragma warning restore CS0618

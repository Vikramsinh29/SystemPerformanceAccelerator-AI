using System.Net;
using System.Text;
using System.Text.Json;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

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
            throw new InvalidOperationException("HTTP must not be called."));
        var service = CreateService(
            location,
            handler,
            new PrefixCredentialProtector());

        var result = await service.GetStatusAsync();

        Assert.False(result.IsActive);
        Assert.Equal("not_activated", result.Status);
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

        public byte[] Protect(byte[] plaintext) => [.. Prefix, .. plaintext];

        public byte[] Unprotect(byte[] protectedData) =>
            protectedData[Prefix.Length..];
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

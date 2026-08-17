using System.Net;
using System.Text;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class ProductionLicensingClientTests
{
    private static readonly Uri BaseUri = new("https://licensing.example.test/");
    private const string Token = "pcspa1.payload.signature";
    private const string Fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task GetAccountLicenseAsync_SendsBearerAndMapsLicense()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://licensing.example.test/account/license", request.RequestUri?.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(Token, request.Headers.Authorization?.Parameter);

            return Json(HttpStatusCode.OK, """
                {
                  "license": {
                    "entitlementId": "ent-1",
                    "accountId": "acct-1",
                    "productId": "pc-spa",
                    "state": "active",
                    "activationLimit": 5,
                    "activeDeviceCount": 2,
                    "periodEndsUtc": "2026-12-31T00:00:00Z",
                    "paymentGraceEndsUtc": null,
                    "offlineValidUntilUtc": "2026-08-20T00:00:00Z",
                    "usable": true
                  }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        var client = new ProductionLicensingClient(httpClient, BaseUri);

        var license = await client.GetAccountLicenseAsync(Token);

        Assert.NotNull(license);
        Assert.Equal("ent-1", license.EntitlementId);
        Assert.Equal("acct-1", license.AccountId);
        Assert.Equal("pc-spa", license.ProductId);
        Assert.Equal(5, license.ActivationLimit);
        Assert.Equal(2, license.ActiveDeviceCount);
        Assert.True(license.Usable);
    }

    [Fact]
    public async Task GetAccountLicenseAsync_ReturnsNullForLicenseNotFound()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.NotFound,
            "{\"error\":\"license_not_found\"}"));

        using var httpClient = new HttpClient(handler);
        var client = new ProductionLicensingClient(httpClient, BaseUri);

        var license = await client.GetAccountLicenseAsync(Token);

        Assert.Null(license);
    }

    [Fact]
    public async Task ActivateDeviceAsync_UsesExpectedRouteAndNormalizedFingerprint()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://licensing.example.test/activate", request.RequestUri?.ToString());
            Assert.Equal(Token, request.Headers.Authorization?.Parameter);

            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("\"deviceFingerprintHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"", body);
            Assert.Contains("\"deviceLabel\":\"Office PC\"", body);

            return Json(HttpStatusCode.OK, """
                {
                  "ok": true,
                  "code": "activated",
                  "license": null
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        var client = new ProductionLicensingClient(httpClient, BaseUri);

        var result = await client.ActivateDeviceAsync(Token, Fingerprint, "  Office PC  ");

        Assert.True(result.Succeeded);
        Assert.Equal("activated", result.Code);
        Assert.Equal(200, result.HttpStatusCode);
    }

    [Fact]
    public async Task DeactivateDeviceAsync_MapsDomainConflictWithoutThrowing()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal("https://licensing.example.test/deactivate", request.RequestUri?.ToString());
            return Json(HttpStatusCode.Conflict, """
                {
                  "error": "activation_conflict",
                  "license": null
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        var client = new ProductionLicensingClient(httpClient, BaseUri);

        var result = await client.DeactivateDeviceAsync(Token, Fingerprint);

        Assert.False(result.Succeeded);
        Assert.Equal("activation_conflict", result.Code);
        Assert.Equal(409, result.HttpStatusCode);
    }

    [Fact]
    public async Task ValidateDeviceAsync_MapsValidationResponse()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal("https://licensing.example.test/validate", request.RequestUri?.ToString());
            return Json(HttpStatusCode.OK, """
                {
                  "valid": false,
                  "code": "device_not_active",
                  "license": null
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        var client = new ProductionLicensingClient(httpClient, BaseUri);

        var result = await client.ValidateDeviceAsync(Token, Fingerprint);

        Assert.False(result.IsValid);
        Assert.Equal("device_not_active", result.Code);
        Assert.Equal(200, result.HttpStatusCode);
    }

    [Fact]
    public async Task ClientRejectsMalformedCredentialsBeforeNetworkAccess()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Network should not be reached."));
        using var httpClient = new HttpClient(handler);
        var client = new ProductionLicensingClient(httpClient, BaseUri);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetAccountLicenseAsync("token with spaces"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ValidateDeviceAsync(Token, "not-a-hash"));

        Assert.Equal(0, handler.CallCount);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(responder(request));
        }
    }
}

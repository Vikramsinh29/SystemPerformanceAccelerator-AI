using System.Net;
using System.Text;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class DesktopApiClientCancellationTests
{
    [Fact]
    public async Task SendAsync_WhenCallerCancels_MapsCancelledFailure()
    {
        var handler = new DelayHandler(TimeSpan.FromMilliseconds(200));
        var client = new DesktopApiClient(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://desktop.test/")
            },
            TimeSpan.FromSeconds(5));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.CancelAfter(20);

        var result = await client.SendAsync<object, object>(
            HttpMethod.Get,
            "api/auth/session",
            request: null,
            bearerToken: null,
            allowRetry: true,
            cancellationSource.Token);

        Assert.False(result.Success);
        Assert.Equal(ApiErrorKind.Cancelled, result.Failure?.Kind);
    }

    [Fact]
    public async Task SendAsync_WhenTimeoutExpires_MapsTimeoutFailure()
    {
        var handler = new DelayHandler(TimeSpan.FromMilliseconds(200));
        var client = new DesktopApiClient(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://desktop.test/")
            },
            TimeSpan.FromMilliseconds(20));

        var result = await client.SendAsync<object, object>(
            HttpMethod.Get,
            "api/auth/session",
            request: null,
            bearerToken: null,
            allowRetry: true,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ApiErrorKind.Timeout, result.Failure?.Kind);
    }

    [Fact]
    public async Task SendAsync_ParsesStructuredValidationFailure()
    {
        var client = new DesktopApiClient(
            new HttpClient(
                new ImmediateHandler(
                    new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                    {
                        Content = new StringContent(
                            "{\"code\":\"invalid_device\",\"message\":\"Device does not match.\"}",
                            Encoding.UTF8,
                            "application/json")
                    }))
            {
                BaseAddress = new Uri("https://desktop.test/")
            },
            TimeSpan.FromSeconds(2));

        var result = await client.SendAsync<object, object>(
            HttpMethod.Post,
            "api/licenses/validate",
            request: new { deviceId = "device-1" },
            bearerToken: "token",
            allowRetry: true,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ApiErrorKind.ValidationFailed, result.Failure?.Kind);
        Assert.Equal("invalid_device", result.Failure?.Code);
        Assert.False(result.Failure?.IsRetryable);
    }

    private sealed class DelayHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        }
    }

    private sealed class ImmediateHandler(HttpResponseMessage response) :
        HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}

using System.Net;
using System.Text;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class ProductionDesktopAuthorizationHandoffCompositionTests
{
    [Fact]
    public void ExchangeUri_IsExactProductionHttpsEndpoint()
    {
        Assert.True(
            ProductionDesktopAuthorizationHandoffComposition
                .ExchangeUri
                .IsAbsoluteUri);

        Assert.Equal(
            Uri.UriSchemeHttps,
            ProductionDesktopAuthorizationHandoffComposition
                .ExchangeUri
                .Scheme);

        Assert.Equal(
            "pc-spa-licensing-v2-production.pc-spa-feedback.workers.dev",
            ProductionDesktopAuthorizationHandoffComposition
                .ExchangeUri
                .Host);

        Assert.Equal(
            "/installation-authorization/exchange",
            ProductionDesktopAuthorizationHandoffComposition
                .ExchangeUri
                .AbsolutePath);

        Assert.True(
            string.IsNullOrEmpty(
                ProductionDesktopAuthorizationHandoffComposition
                    .ExchangeUri
                    .Query));

        Assert.True(
            string.IsNullOrEmpty(
                ProductionDesktopAuthorizationHandoffComposition
                    .ExchangeUri
                    .Fragment));
    }

    [Fact]
    public async Task Composition_UsesRealExchangeClientContract()
    {
        RequestCaptureHandler? handler = null;

        handler =
            new RequestCaptureHandler(
                request =>
                {
                    Assert.Equal(
                        ProductionDesktopAuthorizationHandoffComposition
                            .ExchangeUri,
                        request.RequestUri);

                    Assert.Equal(
                        HttpMethod.Post,
                        request.Method);

                    var body =
                        request.Content!
                            .ReadAsStringAsync()
                            .GetAwaiter()
                            .GetResult();

                    Assert.Contains(
                        "\"authorizationCode\":\"one-time-code\"",
                        body,
                        StringComparison.Ordinal);

                    Assert.DoesNotContain(
                        "accountId",
                        body,
                        StringComparison.OrdinalIgnoreCase);

                    Assert.DoesNotContain(
                        "productId",
                        body,
                        StringComparison.OrdinalIgnoreCase);

                    return new HttpResponseMessage(
                        HttpStatusCode.OK)
                    {
                        Content =
                            new StringContent(
                                """
                                {
                                  "token": "server-issued-bearer",
                                  "tokenType": "Bearer",
                                  "expiresInSeconds": 300
                                }
                                """,
                                Encoding.UTF8,
                                "application/json")
                    };
                });

        using var httpClient =
            new HttpClient(handler);

        var store =
            new RecordingCredentialStore();

        var handoff =
            ProductionDesktopAuthorizationHandoffComposition
                .Create(
                    httpClient,
                    store);

        var result =
            await handoff.HandleAsync(
                "pcspa://authorize#code=one-time-code");

        Assert.True(result.Success);
        Assert.Equal(
            "authorized",
            result.Code);

        Assert.Equal(
            1,
            handler.CallCount);

        Assert.Equal(
            "server-issued-bearer",
            store.SavedBearerToken);

        Assert.NotNull(
            store.SavedExpiresUtc);

        Assert.True(
            store.SavedExpiresUtc >
            DateTimeOffset.UtcNow);

        Assert.NotEqual(
            "one-time-code",
            store.SavedBearerToken);
    }

    [Fact]
    public async Task Composition_FailsClosedWhenProductionExchangeRejectsCode()
    {
        var handler =
            new RequestCaptureHandler(
                _ =>
                    new HttpResponseMessage(
                        HttpStatusCode.Unauthorized)
                    {
                        Content =
                            new StringContent(
                                """
                                {
                                  "error": "invalid_authorization"
                                }
                                """,
                                Encoding.UTF8,
                                "application/json")
                    });

        using var httpClient =
            new HttpClient(handler);

        var store =
            new RecordingCredentialStore();

        var handoff =
            ProductionDesktopAuthorizationHandoffComposition
                .Create(
                    httpClient,
                    store);

        var result =
            await handoff.HandleAsync(
                "pcspa://authorize#code=invalid-code");

        Assert.False(result.Success);

        Assert.Equal(
            "authorization_exchange_failed",
            result.Code);

        Assert.Equal(
            1,
            handler.CallCount);

        Assert.Null(
            store.SavedBearerToken);
    }

    [Fact]
    public async Task Composition_DoesNotSendRequestForInvalidActivationUri()
    {
        var handler =
            new RequestCaptureHandler(
                _ =>
                    throw new InvalidOperationException(
                        "HTTP must not be called."));

        using var httpClient =
            new HttpClient(handler);

        var store =
            new RecordingCredentialStore();

        var handoff =
            ProductionDesktopAuthorizationHandoffComposition
                .Create(
                    httpClient,
                    store);

        var result =
            await handoff.HandleAsync(
                "https://attacker.example/#code=stolen");

        Assert.False(result.Success);

        Assert.Equal(
            0,
            handler.CallCount);

        Assert.Null(
            store.SavedBearerToken);
    }

    [Fact]
    public void Composition_RejectsMissingDependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                ProductionDesktopAuthorizationHandoffComposition
                    .Create(
                        null!));

        Assert.Throws<ArgumentNullException>(
            () =>
                ProductionDesktopAuthorizationHandoffComposition
                    .Create(
                        new HttpClient(),
                        null!));
    }

    private sealed class RequestCaptureHandler :
        HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            HttpResponseMessage>
            _responseFactory;

        public RequestCaptureHandler(
            Func<
                HttpRequestMessage,
                HttpResponseMessage>
                responseFactory)
        {
            _responseFactory =
                responseFactory;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage>
            SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            CallCount++;

            return Task.FromResult(
                _responseFactory(request));
        }
    }

    private sealed class RecordingCredentialStore :
        IDesktopCredentialStore
    {
        public string? SavedBearerToken { get; private set; }

        public DateTimeOffset? SavedExpiresUtc { get; private set; }

        public Task SaveAsync(
            string bearerToken,
            DateTimeOffset expiresUtc,
            CancellationToken cancellationToken = default)
        {
            SavedBearerToken =
                bearerToken;

            SavedExpiresUtc =
                expiresUtc;

            return Task.CompletedTask;
        }

        public Task<DesktopCredential?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DesktopCredential?>(null);

        public Task ClearAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
using System.Net;
using System.Text;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class DesktopAuthorizationFoundationTests
{
    [Fact]
    public void AuthorizationClient_RequiresHttps()
    {
        using var httpClient =
            new HttpClient(
                new StubHandler(
                    HttpStatusCode.OK,
                    "{}"));

        Assert.Throws<ArgumentException>(
            () =>
                new DesktopInstallationAuthorizationClient(
                    httpClient,
                    new Uri(
                        "http://example.test/exchange")));
    }

    [Fact]
    public async Task AuthorizationClient_ExchangesCode()
    {
        const string token =
            "desktop-token-001";

        using var httpClient =
            new HttpClient(
                new StubHandler(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "token": "{{token}}",
                      "tokenType": "Bearer",
                      "expiresInSeconds": 300
                    }
                    """));

        var client =
            new DesktopInstallationAuthorizationClient(
                httpClient,
                new Uri(
                    "https://example.test/exchange"));

        var result =
            await client.ExchangeAsync(
                "one-time-code-001");

        Assert.True(result.Success);
        Assert.Equal(token, result.BearerToken);
        Assert.Equal("authorized", result.Code);
        Assert.NotNull(result.ExpiresUtc);
    }

    [Fact]
    public async Task CredentialStore_DoesNotPersistPlaintextToken()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "pcspa-auth-tests",
                Guid.NewGuid().ToString("N"));

        var path =
            Path.Combine(
                root,
                "credential.dat");

        const string token =
            "desktop-sensitive-token-001";

        try
        {
            var store =
                new WindowsDesktopCredentialStore(
                    path);

            await store.SaveAsync(
                token,
                DateTimeOffset.UtcNow.AddMinutes(5));

            var bytes =
                await File.ReadAllBytesAsync(path);

            var fileText =
                Encoding.UTF8.GetString(bytes);

            Assert.DoesNotContain(
                token,
                fileText,
                StringComparison.Ordinal);

            var loaded =
                await store.LoadAsync();

            Assert.NotNull(loaded);
            Assert.Equal(
                token,
                loaded!.BearerToken);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(
                    root,
                    recursive: true);
            }
        }
    }

    [Fact]
    public async Task CredentialStore_Clear_RemovesCredential()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "pcspa-auth-tests",
                Guid.NewGuid().ToString("N"));

        var path =
            Path.Combine(
                root,
                "credential.dat");

        try
        {
            var store =
                new WindowsDesktopCredentialStore(
                    path);

            await store.SaveAsync(
                "desktop-token-002",
                DateTimeOffset.UtcNow.AddMinutes(5));

            await store.ClearAsync();

            Assert.False(
                File.Exists(path));

            Assert.Null(
                await store.LoadAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(
                    root,
                    recursive: true);
            }
        }
    }

    [Fact]
    public async Task AuthorizationClient_MissingToken_FailsClosed()
    {
        using var httpClient =
            new HttpClient(
                new StubHandler(
                    HttpStatusCode.OK,
                    """
                    {
                      "tokenType": "Bearer",
                      "expiresInSeconds": 300
                    }
                    """));

        var client =
            new DesktopInstallationAuthorizationClient(
                httpClient,
                new Uri(
                    "https://example.test/exchange"));

        var result =
            await client.ExchangeAsync(
                "one-time-code-002");

        Assert.False(result.Success);
        Assert.Null(result.BearerToken);
        Assert.Null(result.ExpiresUtc);
        Assert.Equal(
            "invalid_authorization_response",
            result.Code);
    }

    [Fact]
    public async Task AuthorizationClient_InvalidTokenType_FailsClosed()
    {
        using var httpClient =
            new HttpClient(
                new StubHandler(
                    HttpStatusCode.OK,
                    """
                    {
                      "token": "desktop-token-003",
                      "tokenType": "Basic",
                      "expiresInSeconds": 300
                    }
                    """));

        var client =
            new DesktopInstallationAuthorizationClient(
                httpClient,
                new Uri(
                    "https://example.test/exchange"));

        var result =
            await client.ExchangeAsync(
                "one-time-code-003");

        Assert.False(result.Success);
        Assert.Equal(
            "invalid_authorization_response",
            result.Code);
    }

    [Fact]
    public async Task AuthorizationClient_InvalidExpiry_FailsClosed()
    {
        using var httpClient =
            new HttpClient(
                new StubHandler(
                    HttpStatusCode.OK,
                    """
                    {
                      "token": "desktop-token-004",
                      "tokenType": "Bearer",
                      "expiresInSeconds": 0
                    }
                    """));

        var client =
            new DesktopInstallationAuthorizationClient(
                httpClient,
                new Uri(
                    "https://example.test/exchange"));

        var result =
            await client.ExchangeAsync(
                "one-time-code-004");

        Assert.False(result.Success);
        Assert.Equal(
            "invalid_authorization_response",
            result.Code);
    }

    [Fact]
    public async Task AuthorizationClient_HttpFailure_FailsClosed()
    {
        using var httpClient =
            new HttpClient(
                new StubHandler(
                    HttpStatusCode.Unauthorized,
                    """
                    {
                      "error": "unauthorized"
                    }
                    """));

        var client =
            new DesktopInstallationAuthorizationClient(
                httpClient,
                new Uri(
                    "https://example.test/exchange"));

        var result =
            await client.ExchangeAsync(
                "one-time-code-005");

        Assert.False(result.Success);
        Assert.Null(result.BearerToken);
        Assert.Equal(
            "authorization_exchange_failed",
            result.Code);
    }

    [Fact]
    public async Task CredentialStore_CorruptedPayload_FailsClosed()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "pcspa-auth-tests",
                Guid.NewGuid().ToString("N"));

        var path =
            Path.Combine(
                root,
                "credential.dat");

        try
        {
            Directory.CreateDirectory(root);

            await File.WriteAllTextAsync(
                path,
                "not-a-valid-protected-credential");

            var store =
                new WindowsDesktopCredentialStore(
                    path);

            var loaded =
                await store.LoadAsync();

            Assert.Null(loaded);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(
                    root,
                    recursive: true);
            }
        }
    }
    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string body)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage>
            SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            var response =
                new HttpResponseMessage(
                    statusCode)
                {
                    Content =
                        new StringContent(
                            body,
                            Encoding.UTF8,
                            "application/json")
                };

            return Task.FromResult(response);
        }
    }
}
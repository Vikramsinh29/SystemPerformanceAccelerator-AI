using System.Net;
using System.Text.Json.Serialization;
using System.Text;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class DesktopApiClientCancellationTests
{
    [Fact]
    public async Task SendAsync_ParsesJsonSuccessFromNonSeekableStream()
    {
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new NonSeekableReadStream(
                        Encoding.UTF8.GetBytes(
                            "{\"success\":true,\"name\":\"live\"}")))
            });

        var result = await client.SendAsync<object, SuccessPayload>(
            HttpMethod.Get,
            "api/auth/session",
            request: null,
            bearerToken: null,
            allowRetry: false,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Payload?.Success);
        Assert.Equal("live", result.Payload?.Name);
    }

    [Fact]
    public async Task SendAsync_ParsesNoContentAsSuccessfulEmptyPayload()
    {
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await client.SendAsync<object, SuccessPayload>(
            HttpMethod.Post,
            "api/licenses/deactivate",
            request: null,
            bearerToken: "token",
            allowRetry: false,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task SendAsync_ParsesEmptySuccessBodyAsSuccessfulEmptyPayload()
    {
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            });

        var result = await client.SendAsync<object, SuccessPayload>(
            HttpMethod.Get,
            "api/auth/session",
            request: null,
            bearerToken: null,
            allowRetry: false,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task SendAsync_ParsesJsonSuccessBody()
    {
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"success\":true,\"name\":\"ok\"}",
                    Encoding.UTF8,
                    "application/json")
            });

        var result = await client.SendAsync<object, SuccessPayload>(
            HttpMethod.Get,
            "api/auth/session",
            request: null,
            bearerToken: null,
            allowRetry: false,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Payload?.Success);
        Assert.Equal("ok", result.Payload?.Name);
    }

    [Fact]
    public async Task SendAsync_ExtractsAccountSessionCookieFromSetCookieHeader()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"success\":true,\"name\":\"ok\"}",
                Encoding.UTF8,
                "application/json")
        };
        response.Headers.Add(
            "Set-Cookie",
            "pcspa_session=session-cookie-value; Path=/; HttpOnly; SameSite=Lax; Secure");
        var client = CreateClient(response);

        var result = await client.SendAsync<object, SuccessPayload>(
            HttpMethod.Post,
            "api/auth/login",
            request: null,
            bearerToken: null,
            allowRetry: false,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("session-cookie-value", result.AccountSessionCookie);
    }

    [Fact]
    public async Task SendAsync_AttachesAccountSessionCookieWithoutAuthorizationHeader()
    {
        var handler = new CapturingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"success\":true}",
                    Encoding.UTF8,
                    "application/json")
            });
        var client = new DesktopApiClient(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://desktop.test/")
            },
            TimeSpan.FromSeconds(2));

        var result = await client.SendAsync<object, SuccessPayload>(
            HttpMethod.Get,
            "api/auth/session",
            request: null,
            bearerToken: null,
            allowRetry: false,
            CancellationToken.None,
            accountSessionCookie: "stored-account-session");

        Assert.True(result.Success);
        Assert.Null(handler.AuthorizationHeader);
        Assert.Equal(
            "pcspa_session=stored-account-session",
            handler.CookieHeader);
    }

    [Fact]
    public async Task SendAsync_ParsesJsonErrorBody()
    {
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    "{\"code\":\"WRONG_USER\",\"message\":\"Wrong account.\"}",
                    Encoding.UTF8,
                    "application/json")
            });

        var result = await client.SendAsync<object, SuccessPayload>(
            HttpMethod.Post,
            "api/licenses/activate",
            request: null,
            bearerToken: "token",
            allowRetry: false,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ApiErrorKind.AuthorizationFailed, result.Failure?.Kind);
        Assert.Equal("WRONG_USER", result.Failure?.Code);
        Assert.Equal("Wrong account.", result.Failure?.Message);
    }

    [Fact]
    public async Task SendAsync_MapsNonJsonErrorBodyToStatusFailure()
    {
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "not json",
                    Encoding.UTF8,
                    "text/plain")
            });

        var result = await client.SendAsync<object, SuccessPayload>(
            HttpMethod.Post,
            "api/licenses/activate",
            request: null,
            bearerToken: "token",
            allowRetry: false,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ApiErrorKind.InvalidRequest, result.Failure?.Kind);
        Assert.Null(result.Failure?.Code);
    }

    [Fact]
    public async Task SendAsync_MapsMalformedJsonSuccessBodyToUnexpectedResponse()
    {
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{",
                    Encoding.UTF8,
                    "application/json")
            });

        var result = await client.SendAsync<object, SuccessPayload>(
            HttpMethod.Get,
            "api/auth/session",
            request: null,
            bearerToken: null,
            allowRetry: false,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ApiErrorKind.UnexpectedResponse, result.Failure?.Kind);
    }

    [Fact]
    public async Task SendAsync_WhenCallerCancelsDuringResponseRead_MapsCancelledFailure()
    {
        using var cancellationSource = new CancellationTokenSource();
        var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new DelayedContent(TimeSpan.FromSeconds(5))
            });
        cancellationSource.CancelAfter(20);

        var result = await client.SendAsync<object, SuccessPayload>(
            HttpMethod.Get,
            "api/auth/session",
            request: null,
            bearerToken: null,
            allowRetry: false,
            cancellationSource.Token);

        Assert.False(result.Success);
        Assert.Equal(ApiErrorKind.Cancelled, result.Failure?.Kind);
    }

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

    private static DesktopApiClient CreateClient(
        HttpResponseMessage response) =>
        new(
            new HttpClient(new ImmediateHandler(response))
            {
                BaseAddress = new Uri("https://desktop.test/")
            },
            TimeSpan.FromSeconds(2));

    private sealed record SuccessPayload(
        [property: JsonPropertyName("success")]
        bool Success,
        [property: JsonPropertyName("name")]
        string? Name);

    private sealed class NonSeekableReadStream(byte[] buffer) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length =>
            throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(
            byte[] target,
            int offset,
            int count)
        {
            if (_position >= buffer.Length)
            {
                return 0;
            }

            var bytesToCopy = Math.Min(count, buffer.Length - _position);
            Array.Copy(buffer, _position, target, offset, bytesToCopy);
            _position += bytesToCopy;
            return bytesToCopy;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_position >= buffer.Length)
            {
                return ValueTask.FromResult(0);
            }

            var bytesToCopy = Math.Min(target.Length, buffer.Length - _position);
            buffer.AsMemory(_position, bytesToCopy).CopyTo(target);
            _position += bytesToCopy;
            return ValueTask.FromResult(bytesToCopy);
        }

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] source,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class DelayedContent(TimeSpan delay) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            SerializeToStreamAsync(
                stream,
                context,
                CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            var bytes = Encoding.UTF8.GetBytes("{\"success\":true}");
            await stream.WriteAsync(bytes, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
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

    private sealed class CapturingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) :
        HttpMessageHandler
    {
        public string? AuthorizationHeader { get; private set; }

        public string? CookieHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            CookieHeader = request.Headers.TryGetValues(
                "Cookie",
                out var values)
                ? string.Join("; ", values)
                : null;
            return Task.FromResult(responder(request));
        }
    }
}

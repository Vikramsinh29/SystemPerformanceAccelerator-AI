using System.Net;
using System.Text;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class DiagnosticFeedbackSubmissionServiceTests
{
    [Fact]
    public async Task SubmitAsync_AcceptsOnlyValidCreatedReceipt()
    {
        var handler = new StubHandler(
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    "{\"accepted\":true,\"reference\":\"PCSPA-20260802-ABCDEF1234\"}",
                    Encoding.UTF8,
                    "application/json")
            });
        var service = new DiagnosticFeedbackSubmissionService(
            new HttpClient(handler),
            new Uri("https://feedback.test/v1/feedback"),
            TimeSpan.FromSeconds(2));

        var result = await service.SubmitAsync(CreateReport());

        Assert.True(result.Success);
        Assert.Equal("PCSPA-20260802-ABCDEF1234", result.Reference);
        Assert.Equal(HttpMethod.Post, handler.RequestMethod);
        Assert.Equal("application/json", handler.ContentType);
    }

    [Fact]
    public async Task SubmitAsync_NetworkFailureReturnsLocalFallbackMessage()
    {
        var service = new DiagnosticFeedbackSubmissionService(
            new HttpClient(new ThrowingHandler()),
            new Uri("https://feedback.test/v1/feedback"));

        var result = await service.SubmitAsync(CreateReport());

        Assert.False(result.Success);
        Assert.Null(result.Reference);
        Assert.Contains("local ZIP", result.Message);
    }

    [Fact]
    public async Task SubmitAsync_InvalidReceiptFailsClosed()
    {
        var service = new DiagnosticFeedbackSubmissionService(
            new HttpClient(
                new StubHandler(
                    new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = new StringContent(
                            "{\"accepted\":true,\"reference\":\"invalid\"}")
                    })),
            new Uri("https://feedback.test/v1/feedback"));

        var result = await service.SubmitAsync(CreateReport());

        Assert.False(result.Success);
        Assert.Null(result.Reference);
    }

    [Fact]
    public void Constructor_RejectsNonHttpsEndpoint()
    {
        Assert.Throws<ArgumentException>(
            () => new DiagnosticFeedbackSubmissionService(
                endpoint: new Uri("http://feedback.test/v1/feedback")));
    }

    private static DiagnosticFeedbackSubmissionRequest CreateReport() =>
        new(
            1,
            "1.0.0",
            "test-build",
            "ERR-20260802120000-ABCDEF",
            "Cleaner",
            "The scan stopped.",
            "The scan completes.",
            "Windows test",
            ".NET test",
            false,
            "0123456789abcdef0123456789abcdef",
            []);

    private sealed class StubHandler(HttpResponseMessage response) :
        HttpMessageHandler
    {
        public HttpMethod? RequestMethod { get; private set; }

        public string? ContentType { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestMethod = request.Method;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Offline");
    }
}

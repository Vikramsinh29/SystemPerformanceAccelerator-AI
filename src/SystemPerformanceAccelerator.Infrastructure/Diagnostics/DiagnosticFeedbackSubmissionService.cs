using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Diagnostics;

public sealed class DiagnosticFeedbackSubmissionService :
    IDiagnosticFeedbackSubmissionService
{
    public static readonly Uri ProductionEndpoint = new(
        "https://pc-spa-feedback-api.pc-spa-feedback.workers.dev/v1/feedback");

    private const int MaximumBodyBytes = 64 * 1024;
    private static readonly TimeSpan DefaultTimeout =
        TimeSpan.FromSeconds(20);
    private static readonly Regex ReferencePattern = new(
        "^BETA-[0-9]{8}-[A-F0-9]{10}$",
        RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly HttpClient SharedClient = new();

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;

    public DiagnosticFeedbackSubmissionService(
        HttpClient? httpClient = null,
        Uri? endpoint = null,
        TimeSpan? timeout = null)
    {
        _httpClient = httpClient ?? SharedClient;
        _endpoint = endpoint ?? ProductionEndpoint;
        _timeout = timeout ?? DefaultTimeout;

        if (!string.Equals(
                _endpoint.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The feedback endpoint must use HTTPS.",
                nameof(endpoint));
        }

        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async Task<DiagnosticFeedbackSubmissionResult> SubmitAsync(
        DiagnosticFeedbackSubmissionRequest report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        var json = JsonSerializer.Serialize(report, SerializerOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumBodyBytes)
        {
            return Failure(
                "The reviewed report is too large to send safely. Create the local ZIP instead.");
        }

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                _endpoint)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            };
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Failure(
                    "The feedback service is temporarily limiting requests. Create the local ZIP or try again later.");
            }

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return Failure(
                    "The feedback service did not accept the report. Create the local ZIP instead.");
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(timeoutSource.Token);
            var received = await JsonSerializer.DeserializeAsync<
                SubmissionResponse>(
                stream,
                SerializerOptions,
                timeoutSource.Token);

            if (received?.Accepted != true ||
                string.IsNullOrWhiteSpace(received.Reference) ||
                !ReferencePattern.IsMatch(received.Reference))
            {
                return Failure(
                    "The feedback service returned an invalid receipt. Create the local ZIP instead.");
            }

            return new DiagnosticFeedbackSubmissionResult(
                true,
                received.Reference,
                $"Privacy-safe error report sent. Reference: {received.Reference}");
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                "The feedback service did not respond in time. Create the local ZIP or try again later.");
        }
        catch (HttpRequestException)
        {
            return Failure(
                "PC-SPA could not reach the feedback service. Check the connection or create the local ZIP.");
        }
        catch (JsonException)
        {
            return Failure(
                "The feedback service returned an unreadable receipt. Create the local ZIP instead.");
        }
    }

    private static DiagnosticFeedbackSubmissionResult Failure(
        string message) =>
        new(false, null, message);

    private sealed record SubmissionResponse(
        bool Accepted,
        string? Reference);
}

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class DesktopApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull
        };

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;
    private readonly IDesktopApiLogger _logger;

    public DesktopApiClient(
        HttpClient httpClient,
        TimeSpan timeout,
        IDesktopApiLogger? logger = null)
    {
        _httpClient = httpClient ??
            throw new ArgumentNullException(nameof(httpClient));
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _timeout = timeout;
        _logger = logger ?? NullDesktopApiLogger.Instance;
    }

    public JsonSerializerOptions Serializer => SerializerOptions;

    public async Task<ApiResponse<TResponse>> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string relativePath,
        TRequest? request,
        string? bearerToken,
        bool allowRetry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException(
                "A relative API path is required.",
                nameof(relativePath));
        }

        var attempts = allowRetry ? 2 : 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeoutSource.CancelAfter(_timeout);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var message = CreateRequest(
                    method,
                    relativePath,
                    request,
                    bearerToken);
                using var response = await _httpClient.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutSource.Token);
                stopwatch.Stop();

                var parsed = await ParseResponseAsync<TResponse>(
                    response,
                    timeoutSource.Token);
                if (parsed.Success)
                {
                    _logger.Log(new DesktopApiLogEntry(
                        DesktopApiLogLevel.Information,
                        "desktop_api_response",
                        method.Method,
                        relativePath,
                        response.StatusCode,
                        "API call completed.",
                        null,
                        attempt,
                        stopwatch.Elapsed));
                    return parsed;
                }

                _logger.Log(new DesktopApiLogEntry(
                    parsed.Failure?.IsRetryable == true &&
                    attempt < attempts
                        ? DesktopApiLogLevel.Warning
                        : DesktopApiLogLevel.Error,
                    "desktop_api_response",
                    method.Method,
                    relativePath,
                    response.StatusCode,
                    parsed.Failure?.Message ??
                        "API call failed.",
                    parsed.Failure?.Code,
                    attempt,
                    stopwatch.Elapsed));

                if (!(parsed.Failure?.IsRetryable == true &&
                      attempt < attempts))
                {
                    return parsed;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return ApiResponse<TResponse>.FailureResult(
                    new ApiFailure(
                        ApiErrorKind.Cancelled,
                        null,
                        null,
                        "The operation was cancelled.",
                        false));
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                return ApiResponse<TResponse>.FailureResult(
                    new ApiFailure(
                        ApiErrorKind.Timeout,
                        null,
                        null,
                        "The API request timed out.",
                        false));
            }
            catch (HttpRequestException)
            {
                stopwatch.Stop();
                var failure = new ApiFailure(
                    ApiErrorKind.NetworkUnavailable,
                    null,
                    null,
                    "The API could not be reached.",
                    allowRetry && attempt < attempts);
                _logger.Log(new DesktopApiLogEntry(
                    failure.IsRetryable
                        ? DesktopApiLogLevel.Warning
                        : DesktopApiLogLevel.Error,
                    "desktop_api_network_failure",
                    method.Method,
                    relativePath,
                    null,
                    failure.Message,
                    null,
                    attempt,
                    stopwatch.Elapsed));
                if (!failure.IsRetryable)
                {
                    return ApiResponse<TResponse>.FailureResult(failure);
                }
            }
        }

        return ApiResponse<TResponse>.FailureResult(
            new ApiFailure(
                ApiErrorKind.Transient,
                null,
                null,
                "The API request failed after retry.",
                false));
    }

    private HttpRequestMessage CreateRequest<TRequest>(
        HttpMethod method,
        string relativePath,
        TRequest? request,
        string? bearerToken)
    {
        var message = new HttpRequestMessage(method, relativePath);
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            message.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    bearerToken);
        }

        if (request is not null)
        {
            message.Content = new StringContent(
                JsonSerializer.Serialize(request, SerializerOptions),
                Encoding.UTF8,
                "application/json");
        }

        return message;
    }

    private static async Task<ApiResponse<TResponse>> ParseResponseAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return response.IsSuccessStatusCode
                ? ApiResponse<TResponse>.SuccessResult(default)
                : ApiResponse<TResponse>.FailureResult(
                    MapFailure(response.StatusCode, null));
        }

        if (response.StatusCode == HttpStatusCode.NoContent ||
            response.Content.Headers.ContentLength == 0)
        {
            return response.IsSuccessStatusCode
                ? ApiResponse<TResponse>.SuccessResult(default)
                : ApiResponse<TResponse>.FailureResult(
                    MapFailure(response.StatusCode, null));
        }

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return response.IsSuccessStatusCode
                ? ApiResponse<TResponse>.SuccessResult(default)
                : ApiResponse<TResponse>.FailureResult(
                    MapFailure(response.StatusCode, null));
        }

        if (response.IsSuccessStatusCode)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var payload = JsonSerializer.Deserialize<TResponse>(
                    responseBody,
                    SerializerOptions);
                return ApiResponse<TResponse>.SuccessResult(payload);
            }
            catch (JsonException)
            {
                return ApiResponse<TResponse>.FailureResult(
                    new ApiFailure(
                        ApiErrorKind.UnexpectedResponse,
                        response.StatusCode,
                        null,
                        "The API returned an unreadable success response.",
                        false));
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var error = JsonSerializer.Deserialize<ApiErrorEnvelope>(
                responseBody,
                SerializerOptions);
            return ApiResponse<TResponse>.FailureResult(
                MapFailure(response.StatusCode, error));
        }
        catch (JsonException)
        {
            return ApiResponse<TResponse>.FailureResult(
                MapFailure(response.StatusCode, null));
        }
    }

    private static ApiFailure MapFailure(
        HttpStatusCode statusCode,
        ApiErrorEnvelope? error)
    {
        var code = error?.Code ?? error?.Error;
        var message = error?.Message ??
            $"The API request failed with status {(int)statusCode}.";

        return statusCode switch
        {
            HttpStatusCode.BadRequest => new ApiFailure(
                ApiErrorKind.InvalidRequest,
                statusCode,
                code,
                message,
                false),
            HttpStatusCode.Unauthorized => new ApiFailure(
                ApiErrorKind.AuthenticationFailed,
                statusCode,
                code,
                message,
                false),
            HttpStatusCode.Forbidden => new ApiFailure(
                ApiErrorKind.AuthorizationFailed,
                statusCode,
                code,
                message,
                false),
            HttpStatusCode.NotFound => new ApiFailure(
                ApiErrorKind.NotFound,
                statusCode,
                code,
                message,
                false),
            HttpStatusCode.Conflict => new ApiFailure(
                ApiErrorKind.Conflict,
                statusCode,
                code,
                message,
                false),
            HttpStatusCode.UnprocessableEntity => new ApiFailure(
                ApiErrorKind.ValidationFailed,
                statusCode,
                code,
                message,
                false),
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests => new ApiFailure(
                ApiErrorKind.Transient,
                statusCode,
                code,
                message,
                true),
            _ when (int)statusCode >= 500 => new ApiFailure(
                ApiErrorKind.ServerError,
                statusCode,
                code,
                message,
                true),
            _ => new ApiFailure(
                ApiErrorKind.Unknown,
                statusCode,
                code,
                message,
                false)
        };
    }

    private sealed record ApiErrorEnvelope(
        string? Error,
        string? Code,
        string? Message);

    public sealed record ApiResponse<TResponse>(
        bool Success,
        TResponse? Payload,
        ApiFailure? Failure)
    {
        public static ApiResponse<TResponse> SuccessResult(
            TResponse? payload) =>
            new(true, payload, null);

        public static ApiResponse<TResponse> FailureResult(
            ApiFailure failure) =>
            new(false, default, failure);
    }
}

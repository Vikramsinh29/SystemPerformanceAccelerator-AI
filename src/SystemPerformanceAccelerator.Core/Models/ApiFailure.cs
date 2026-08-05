using System.Net;

namespace SystemPerformanceAccelerator.Core.Models;

public sealed record ApiFailure(
    ApiErrorKind Kind,
    HttpStatusCode? StatusCode,
    string? Code,
    string Message,
    bool IsRetryable);

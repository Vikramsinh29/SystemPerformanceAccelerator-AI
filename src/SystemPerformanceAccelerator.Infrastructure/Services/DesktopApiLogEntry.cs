using System.Net;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed record DesktopApiLogEntry(
    DesktopApiLogLevel Level,
    string EventName,
    string Method,
    string Path,
    HttpStatusCode? StatusCode,
    string Message,
    string? ErrorCode,
    int Attempt,
    TimeSpan Duration);

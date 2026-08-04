namespace SystemPerformanceAccelerator.Core.Models;

public sealed record AuthLoginResult(
    bool Success,
    string? SessionToken,
    AuthSession? Session,
    ApiFailure? Failure);

namespace SystemPerformanceAccelerator.Core.Models;

public sealed record AuthSessionResult(
    bool Success,
    AuthSession? Session,
    ApiFailure? Failure);

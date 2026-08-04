namespace SystemPerformanceAccelerator.Core.Models;

public sealed record RemoteOperationResult(
    bool Success,
    ApiFailure? Failure);

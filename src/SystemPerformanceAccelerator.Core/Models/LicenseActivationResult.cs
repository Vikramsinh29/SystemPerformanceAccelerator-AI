namespace SystemPerformanceAccelerator.Core.Models;

public sealed record LicenseActivationResult(
    bool Success,
    string? LicenseToken,
    LicenseStatus? License,
    ApiFailure? Failure);

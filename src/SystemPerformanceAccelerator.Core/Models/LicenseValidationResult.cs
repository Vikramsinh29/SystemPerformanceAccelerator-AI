namespace SystemPerformanceAccelerator.Core.Models;

public sealed record LicenseValidationResult(
    bool Success,
    LicenseStatus? License,
    ApiFailure? Failure);

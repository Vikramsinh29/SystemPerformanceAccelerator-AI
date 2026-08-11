namespace SystemPerformanceAccelerator.Core.Models;

public sealed record LicenseStatus(
    string? LicenseId,
    string? Plan,
    string? Status,
    string? DeviceId,
    DateTimeOffset? ActivatedUtc,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset? ValidatedUtc);

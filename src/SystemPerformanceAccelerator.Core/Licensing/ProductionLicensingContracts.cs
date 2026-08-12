namespace SystemPerformanceAccelerator.Core.Licensing;

public sealed record ProductionLicenseSnapshot(
    string EntitlementId,
    string AccountId,
    string ProductId,
    string State,
    int ActivationLimit,
    int ActiveDeviceCount,
    DateTimeOffset? PeriodEndsUtc,
    DateTimeOffset? PaymentGraceEndsUtc,
    DateTimeOffset? OfflineValidUntilUtc,
    bool Usable);

public sealed record ProductionLicensingMutationResult(
    bool Succeeded,
    string Code,
    int HttpStatusCode,
    ProductionLicenseSnapshot? License);

public sealed record ProductionDeviceValidationResult(
    bool IsValid,
    string Code,
    int HttpStatusCode,
    ProductionLicenseSnapshot? License);

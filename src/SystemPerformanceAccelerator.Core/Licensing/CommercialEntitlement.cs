namespace SystemPerformanceAccelerator.Core.Licensing;

public sealed record CommercialEntitlement(
    string EntitlementId,
    string AccountId,
    string ProductId,
    CommercialEntitlementState State,
    int SeatLimit,
    int ActiveDeviceCount,
    DateTimeOffset PeriodEndsUtc,
    DateTimeOffset? PaymentGraceEndsUtc,
    DateTimeOffset? OfflineValidUntilUtc,
    int TransfersUsedInRollingWindow,
    DateTimeOffset TransferWindowStartedUtc,
    DateTimeOffset? LastTransferUtc);
namespace SystemPerformanceAccelerator.Core.Licensing;

public sealed record VerifiedPaymentEvent(
    string Provider,
    string ProviderEventId,
    string AccountId,
    string ProductId,
    string? ProviderSubscriptionId,
    VerifiedPaymentEventKind Kind,
    DateTimeOffset OccurredUtc,
    DateTimeOffset? CurrentPeriodEndsUtc,
    int SeatCount,
    string Currency,
    long AmountMinorUnits);
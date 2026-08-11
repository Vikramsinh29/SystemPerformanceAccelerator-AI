namespace SystemPerformanceAccelerator.Core.Licensing;

public sealed record LicensingAuditEvent(
    DateTimeOffset OccurredUtc,
    string Provider,
    string ProviderEventId,
    string AccountId,
    string ProductId,
    VerifiedPaymentEventKind EventKind,
    PaymentEventProcessingStatus Result,
    CommercialEntitlementState? PreviousState,
    CommercialEntitlementState? CurrentState,
    string Message);
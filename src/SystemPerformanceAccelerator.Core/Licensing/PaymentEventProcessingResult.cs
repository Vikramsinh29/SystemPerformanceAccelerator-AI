namespace SystemPerformanceAccelerator.Core.Licensing;

public sealed record PaymentEventProcessingResult(
    PaymentEventProcessingStatus Status,
    CommercialEntitlement? Entitlement,
    string Message);
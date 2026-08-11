namespace SystemPerformanceAccelerator.Core.Licensing;

public enum VerifiedPaymentEventKind
{
    PurchaseCompleted,
    RenewalSucceeded,
    RenewalFailed,
    SubscriptionCancelled,
    Refunded,
    Disputed
}
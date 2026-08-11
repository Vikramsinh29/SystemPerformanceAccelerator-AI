using SystemPerformanceAccelerator.Core.Licensing;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class SimulatedPaymentEventProcessorTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-11T12:00:00Z");

    [Fact]
    public void Purchase_CreatesActiveEntitlement()
    {
        var processor = new SimulatedPaymentEventProcessor();

        var result = processor.Process(Event(
            "purchase-1",
            VerifiedPaymentEventKind.PurchaseCompleted,
            periodEnds: Now.AddYears(1)));

        Assert.Equal(PaymentEventProcessingStatus.Applied, result.Status);
        Assert.Equal(CommercialEntitlementState.Active, result.Entitlement?.State);
        Assert.Equal(1, result.Entitlement?.SeatLimit);
        Assert.Equal(Now.AddDays(30), result.Entitlement?.OfflineValidUntilUtc);
    }

    [Fact]
    public void DuplicateProviderEvent_IsAppliedOnlyOnce()
    {
        var processor = new SimulatedPaymentEventProcessor();
        var paymentEvent = Event(
            "purchase-1",
            VerifiedPaymentEventKind.PurchaseCompleted,
            periodEnds: Now.AddYears(1));

        var first = processor.Process(paymentEvent);
        var duplicate = processor.Process(paymentEvent);

        Assert.Equal(PaymentEventProcessingStatus.Applied, first.Status);
        Assert.Equal(PaymentEventProcessingStatus.Duplicate, duplicate.Status);
        Assert.Single(processor.AuditEvents);
    }

    [Fact]
    public void RenewalFailure_EntersSevenDayGrace()
    {
        var processor = WithPurchase();

        var result = processor.Process(Event(
            "renewal-failed-1",
            VerifiedPaymentEventKind.RenewalFailed,
            occurredUtc: Now.AddDays(30)));

        Assert.Equal(CommercialEntitlementState.Grace, result.Entitlement?.State);
        Assert.Equal(Now.AddDays(37), result.Entitlement?.PaymentGraceEndsUtc);
    }

    [Fact]
    public void RenewalSuccess_RestoresActiveAndRefreshesOfflineWindow()
    {
        var processor = WithPurchase();
        processor.Process(Event(
            "renewal-failed-1",
            VerifiedPaymentEventKind.RenewalFailed,
            occurredUtc: Now.AddDays(30)));

        var result = processor.Process(Event(
            "renewal-success-1",
            VerifiedPaymentEventKind.RenewalSucceeded,
            occurredUtc: Now.AddDays(31),
            periodEnds: Now.AddYears(2)));

        Assert.Equal(CommercialEntitlementState.Active, result.Entitlement?.State);
        Assert.Null(result.Entitlement?.PaymentGraceEndsUtc);
        Assert.Equal(Now.AddDays(61), result.Entitlement?.OfflineValidUntilUtc);
    }

    [Fact]
    public void RefundAndDispute_StopOfflineUseImmediately()
    {
        var refunded = WithPurchase().Process(Event(
            "refund-1",
            VerifiedPaymentEventKind.Refunded,
            occurredUtc: Now.AddDays(1)));
        var disputedProcessor = WithPurchase();
        var disputed = disputedProcessor.Process(Event(
            "dispute-1",
            VerifiedPaymentEventKind.Disputed,
            occurredUtc: Now.AddDays(1)));

        Assert.Equal(CommercialEntitlementState.Refunded, refunded.Entitlement?.State);
        Assert.Equal(Now.AddDays(1), refunded.Entitlement?.OfflineValidUntilUtc);
        Assert.Equal(CommercialEntitlementState.Suspended, disputed.Entitlement?.State);
        Assert.Equal(Now.AddDays(1), disputed.Entitlement?.OfflineValidUntilUtc);
    }

    [Fact]
    public void Cancellation_PreservesAccessUntilRecordedPeriodEnd()
    {
        var processor = WithPurchase();

        var result = processor.Process(Event(
            "cancel-1",
            VerifiedPaymentEventKind.SubscriptionCancelled,
            occurredUtc: Now.AddDays(5),
            periodEnds: Now.AddMonths(6)));

        Assert.Equal(CommercialEntitlementState.Active, result.Entitlement?.State);
        Assert.Equal(Now.AddMonths(6), result.Entitlement?.PeriodEndsUtc);
    }

    [Fact]
    public void OlderEvent_IsIgnoredAndAudited()
    {
        var processor = WithPurchase();
        processor.Process(Event(
            "renewal-1",
            VerifiedPaymentEventKind.RenewalSucceeded,
            occurredUtc: Now.AddDays(10),
            periodEnds: Now.AddYears(2)));

        var result = processor.Process(Event(
            "late-failure",
            VerifiedPaymentEventKind.RenewalFailed,
            occurredUtc: Now.AddDays(9)));

        Assert.Equal(PaymentEventProcessingStatus.IgnoredOutOfOrder, result.Status);
        Assert.Equal(CommercialEntitlementState.Active, result.Entitlement?.State);
        Assert.Equal(PaymentEventProcessingStatus.IgnoredOutOfOrder,
            processor.AuditEvents[^1].Result);
    }

    [Fact]
    public void EventWithoutRequiredEntitlement_CanBeRetriedAfterPurchase()
    {
        var processor = new SimulatedPaymentEventProcessor();
        var refund = Event(
            "refund-1",
            VerifiedPaymentEventKind.Refunded,
            occurredUtc: Now.AddDays(1));

        var early = processor.Process(refund);
        processor.Process(Event(
            "purchase-1",
            VerifiedPaymentEventKind.PurchaseCompleted,
            periodEnds: Now.AddYears(1)));
        var retried = processor.Process(refund);

        Assert.Equal(PaymentEventProcessingStatus.Rejected, early.Status);
        Assert.Null(early.Entitlement);
        Assert.Equal(PaymentEventProcessingStatus.Applied, retried.Status);
        Assert.Equal(CommercialEntitlementState.Refunded,
            retried.Entitlement?.State);
    }

    [Fact]
    public void InvalidNormalizedEvent_IsRejectedWithoutReceipt()
    {
        var processor = new SimulatedPaymentEventProcessor();
        var invalid = Event(
            "invalid-1",
            VerifiedPaymentEventKind.PurchaseCompleted,
            periodEnds: Now.AddYears(1)) with { SeatCount = 0 };

        var first = processor.Process(invalid);
        var retried = processor.Process(invalid);

        Assert.Equal(PaymentEventProcessingStatus.Rejected, first.Status);
        Assert.Equal(PaymentEventProcessingStatus.Rejected, retried.Status);
        Assert.Equal(2, processor.AuditEvents.Count);
    }

    private static SimulatedPaymentEventProcessor WithPurchase()
    {
        var processor = new SimulatedPaymentEventProcessor();
        processor.Process(Event(
            "purchase-1",
            VerifiedPaymentEventKind.PurchaseCompleted,
            periodEnds: Now.AddYears(1)));
        return processor;
    }

    private static VerifiedPaymentEvent Event(
        string id,
        VerifiedPaymentEventKind kind,
        DateTimeOffset? occurredUtc = null,
        DateTimeOffset? periodEnds = null) =>
        new(
            "simulated",
            id,
            "account-1",
            "pcspa-pro",
            "subscription-1",
            kind,
            occurredUtc ?? Now,
            periodEnds,
            SeatCount: 1,
            Currency: "INR",
            AmountMinorUnits: 49900);
}
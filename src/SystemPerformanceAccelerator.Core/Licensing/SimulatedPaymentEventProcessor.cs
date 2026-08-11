namespace SystemPerformanceAccelerator.Core.Licensing;

public sealed class SimulatedPaymentEventProcessor
{
    private readonly object _sync = new();
    private readonly HashSet<string> _processedEvents =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, CommercialEntitlement> _entitlements =
        new(StringComparer.Ordinal);
    private readonly List<LicensingAuditEvent> _auditEvents = [];

    public IReadOnlyList<LicensingAuditEvent> AuditEvents
    {
        get
        {
            lock (_sync)
            {
                return _auditEvents.ToArray();
            }
        }
    }

    public CommercialEntitlement? FindEntitlement(
        string accountId,
        string productId)
    {
        lock (_sync)
        {
            return _entitlements.GetValueOrDefault(Key(accountId, productId));
        }
    }

    public PaymentEventProcessingResult Process(VerifiedPaymentEvent paymentEvent)
    {
        lock (_sync)
        {
            if (!CommercialLicensePolicy.IsNormalizedPaymentEventValid(paymentEvent))
            {
                return AuditAndReturn(
                    paymentEvent,
                    PaymentEventProcessingStatus.Rejected,
                    null,
                    null,
                    "Normalized payment event is invalid.");
            }

            var receiptKey = $"{paymentEvent.Provider}:{paymentEvent.ProviderEventId}";
            if (!_processedEvents.Add(receiptKey))
            {
                return new PaymentEventProcessingResult(
                    PaymentEventProcessingStatus.Duplicate,
                    FindUnsafe(paymentEvent.AccountId, paymentEvent.ProductId),
                    "Provider event was already processed.");
            }

            var key = Key(paymentEvent.AccountId, paymentEvent.ProductId);
            var current = _entitlements.GetValueOrDefault(key);
            if (current?.LastCommercialEventUtc is { } lastEvent &&
                paymentEvent.OccurredUtc < lastEvent)
            {
                return AuditAndReturn(
                    paymentEvent,
                    PaymentEventProcessingStatus.IgnoredOutOfOrder,
                    current,
                    current,
                    "Older provider event was ignored.");
            }

            var transition = Transition(current, paymentEvent);
            if (transition.Status == PaymentEventProcessingStatus.Applied &&
                transition.Entitlement is { } updated)
            {
                _entitlements[key] = updated;
            }
            else if (transition.Status == PaymentEventProcessingStatus.Rejected)
            {
                // A valid event may arrive before its prerequisite event.
                // Do not consume its idempotency key until it can be applied.
                _processedEvents.Remove(receiptKey);
            }

            return AuditAndReturn(
                paymentEvent,
                transition.Status,
                current,
                transition.Entitlement ?? current,
                transition.Message);
        }
    }

    private static PaymentEventProcessingResult Transition(
        CommercialEntitlement? current,
        VerifiedPaymentEvent paymentEvent) =>
        paymentEvent.Kind switch
        {
            VerifiedPaymentEventKind.PurchaseCompleted or
            VerifiedPaymentEventKind.RenewalSucceeded =>
                Activate(current, paymentEvent),
            VerifiedPaymentEventKind.RenewalFailed =>
                BeginPaymentGrace(current, paymentEvent),
            VerifiedPaymentEventKind.SubscriptionCancelled =>
                RecordCancellation(current, paymentEvent),
            VerifiedPaymentEventKind.Refunded =>
                SetTerminalState(
                    current,
                    paymentEvent,
                    CommercialEntitlementState.Refunded,
                    "Entitlement was refunded."),
            VerifiedPaymentEventKind.Disputed =>
                SetTerminalState(
                    current,
                    paymentEvent,
                    CommercialEntitlementState.Suspended,
                    "Entitlement was suspended because the payment was disputed."),
            _ => new PaymentEventProcessingResult(
                PaymentEventProcessingStatus.Rejected,
                current,
                "Payment event kind is unsupported.")
        };

    private static PaymentEventProcessingResult Activate(
        CommercialEntitlement? current,
        VerifiedPaymentEvent paymentEvent)
    {
        if (paymentEvent.CurrentPeriodEndsUtc is not { } periodEnds ||
            periodEnds <= paymentEvent.OccurredUtc)
        {
            return Rejected(current, "Active period end is missing or invalid.");
        }

        var offlineUntil = Min(
            paymentEvent.OccurredUtc + CommercialLicensePolicy.OfflinePeriod,
            periodEnds);
        var updated = new CommercialEntitlement(
            current?.EntitlementId ??
                $"simulated:{paymentEvent.AccountId}:{paymentEvent.ProductId}",
            paymentEvent.AccountId,
            paymentEvent.ProductId,
            CommercialEntitlementState.Active,
            paymentEvent.SeatCount,
            Math.Min(current?.ActiveDeviceCount ?? 0, paymentEvent.SeatCount),
            periodEnds,
            PaymentGraceEndsUtc: null,
            OfflineValidUntilUtc: offlineUntil,
            current?.TransfersUsedInRollingWindow ?? 0,
            current?.TransferWindowStartedUtc ?? paymentEvent.OccurredUtc,
            current?.LastTransferUtc,
            paymentEvent.OccurredUtc);

        return Applied(updated, "Entitlement is active.");
    }

    private static PaymentEventProcessingResult BeginPaymentGrace(
        CommercialEntitlement? current,
        VerifiedPaymentEvent paymentEvent)
    {
        if (current is null)
        {
            return Rejected(null, "Renewal failure has no existing entitlement.");
        }

        var updated = current with
        {
            State = CommercialEntitlementState.Grace,
            PaymentGraceEndsUtc = paymentEvent.OccurredUtc +
                CommercialLicensePolicy.PaymentGracePeriod,
            LastCommercialEventUtc = paymentEvent.OccurredUtc
        };
        return Applied(updated, "Entitlement entered payment grace.");
    }

    private static PaymentEventProcessingResult RecordCancellation(
        CommercialEntitlement? current,
        VerifiedPaymentEvent paymentEvent)
    {
        if (current is null)
        {
            return Rejected(null, "Cancellation has no existing entitlement.");
        }

        var periodEnds = paymentEvent.CurrentPeriodEndsUtc ?? current.PeriodEndsUtc;
        var updated = current with
        {
            PeriodEndsUtc = periodEnds,
            LastCommercialEventUtc = paymentEvent.OccurredUtc
        };
        return Applied(updated, "Cancellation is recorded for period end.");
    }

    private static PaymentEventProcessingResult SetTerminalState(
        CommercialEntitlement? current,
        VerifiedPaymentEvent paymentEvent,
        CommercialEntitlementState state,
        string message)
    {
        if (current is null)
        {
            return Rejected(null, "Terminal event has no existing entitlement.");
        }

        return Applied(
            current with
            {
                State = state,
                PaymentGraceEndsUtc = null,
                OfflineValidUntilUtc = paymentEvent.OccurredUtc,
                LastCommercialEventUtc = paymentEvent.OccurredUtc
            },
            message);
    }

    private PaymentEventProcessingResult AuditAndReturn(
        VerifiedPaymentEvent paymentEvent,
        PaymentEventProcessingStatus status,
        CommercialEntitlement? previous,
        CommercialEntitlement? current,
        string message)
    {
        _auditEvents.Add(new LicensingAuditEvent(
            paymentEvent.OccurredUtc,
            paymentEvent.Provider,
            paymentEvent.ProviderEventId,
            paymentEvent.AccountId,
            paymentEvent.ProductId,
            paymentEvent.Kind,
            status,
            previous?.State,
            current?.State,
            message));
        return new PaymentEventProcessingResult(status, current, message);
    }

    private CommercialEntitlement? FindUnsafe(string accountId, string productId) =>
        _entitlements.GetValueOrDefault(Key(accountId, productId));

    private static PaymentEventProcessingResult Applied(
        CommercialEntitlement entitlement,
        string message) =>
        new(PaymentEventProcessingStatus.Applied, entitlement, message);

    private static PaymentEventProcessingResult Rejected(
        CommercialEntitlement? entitlement,
        string message) =>
        new(PaymentEventProcessingStatus.Rejected, entitlement, message);

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private static string Key(string accountId, string productId) =>
        $"{accountId}\n{productId}";
}
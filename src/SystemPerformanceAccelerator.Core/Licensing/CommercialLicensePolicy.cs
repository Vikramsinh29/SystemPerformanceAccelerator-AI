namespace SystemPerformanceAccelerator.Core.Licensing;

public static class CommercialLicensePolicy
{
    public const int SelfServiceTransfersPerRollingYear = 2;
    public static readonly TimeSpan TransferWindow = TimeSpan.FromDays(365);
    public static readonly TimeSpan TransferCooldown = TimeSpan.FromDays(7);
    public static readonly TimeSpan OfflinePeriod = TimeSpan.FromDays(30);
    public static readonly TimeSpan OfflineWarningPeriod = TimeSpan.FromDays(7);
    public static readonly TimeSpan PaymentGracePeriod = TimeSpan.FromDays(7);

    public static CommercialAccessDecision Evaluate(
        CommercialEntitlement? entitlement,
        DateTimeOffset nowUtc)
    {
        if (!IsStructurallyValid(entitlement))
        {
            return CommercialAccessDecision.Denied("Entitlement data is invalid.");
        }

        var value = entitlement!;
        var offlineAllowed = value.OfflineValidUntilUtc is { } offlineUntil &&
            nowUtc < offlineUntil;
        var offlineWarning = offlineAllowed &&
            value.OfflineValidUntilUtc!.Value - nowUtc <= OfflineWarningPeriod;

        if (value.State == CommercialEntitlementState.Grace)
        {
            if (value.PaymentGraceEndsUtc is not { } graceEnds || nowUtc >= graceEnds)
            {
                return CommercialAccessDecision.Denied("Payment grace has ended.");
            }

            return new CommercialAccessDecision(
                ExistingDeviceAllowed: true,
                NewActivationAllowed: false,
                DeviceTransferAllowed: false,
                OfflineUseAllowed: offlineAllowed,
                OfflineExpiryWarningRequired: offlineWarning,
                Reason: "Existing devices remain available during payment grace.");
        }

        if (value.State != CommercialEntitlementState.Active ||
            nowUtc >= value.PeriodEndsUtc)
        {
            return CommercialAccessDecision.Denied("Entitlement is not active.");
        }

        var activationAllowed = value.ActiveDeviceCount < value.SeatLimit;
        var transferAllowed = IsTransferAllowed(value, nowUtc);

        return new CommercialAccessDecision(
            ExistingDeviceAllowed: true,
            NewActivationAllowed: activationAllowed,
            DeviceTransferAllowed: transferAllowed,
            OfflineUseAllowed: offlineAllowed,
            OfflineExpiryWarningRequired: offlineWarning,
            Reason: "Entitlement is active.");
    }

    public static bool IsSameDeviceReinstallAllowed(
        bool deviceWasPreviouslyRegistered,
        CommercialAccessDecision decision) =>
        deviceWasPreviouslyRegistered && decision.ExistingDeviceAllowed;

    public static bool IsNormalizedPaymentEventValid(
        VerifiedPaymentEvent? paymentEvent)
    {
        if (paymentEvent is null ||
            string.IsNullOrWhiteSpace(paymentEvent.Provider) ||
            string.IsNullOrWhiteSpace(paymentEvent.ProviderEventId) ||
            string.IsNullOrWhiteSpace(paymentEvent.AccountId) ||
            string.IsNullOrWhiteSpace(paymentEvent.ProductId) ||
            paymentEvent.SeatCount < 1 ||
            paymentEvent.AmountMinorUnits < 0 ||
            paymentEvent.Currency.Length != 3 ||
            !paymentEvent.Currency.All(char.IsAsciiLetterUpper))
        {
            return false;
        }

        return Enum.IsDefined(paymentEvent.Kind);
    }

    private static bool IsTransferAllowed(
        CommercialEntitlement entitlement,
        DateTimeOffset nowUtc)
    {
        var windowExpired = nowUtc - entitlement.TransferWindowStartedUtc >=
            TransferWindow;
        var transfersAvailable = windowExpired ||
            entitlement.TransfersUsedInRollingWindow <
                SelfServiceTransfersPerRollingYear;
        var cooldownComplete = entitlement.LastTransferUtc is not { } lastTransfer ||
            nowUtc - lastTransfer >= TransferCooldown;

        return transfersAvailable && cooldownComplete;
    }

    private static bool IsStructurallyValid(
        CommercialEntitlement? entitlement) =>
        entitlement is not null &&
        !string.IsNullOrWhiteSpace(entitlement.EntitlementId) &&
        !string.IsNullOrWhiteSpace(entitlement.AccountId) &&
        !string.IsNullOrWhiteSpace(entitlement.ProductId) &&
        Enum.IsDefined(entitlement.State) &&
        entitlement.SeatLimit > 0 &&
        entitlement.ActiveDeviceCount >= 0 &&
        entitlement.ActiveDeviceCount <= entitlement.SeatLimit &&
        entitlement.TransfersUsedInRollingWindow >= 0;
}
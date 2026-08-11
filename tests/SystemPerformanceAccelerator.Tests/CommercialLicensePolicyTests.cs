using SystemPerformanceAccelerator.Core.Licensing;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class CommercialLicensePolicyTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-11T12:00:00Z");

    [Fact]
    public void ActiveEntitlement_AllowsExistingDeviceAndAvailableSeat()
    {
        var decision = CommercialLicensePolicy.Evaluate(Create(), Now);

        Assert.True(decision.ExistingDeviceAllowed);
        Assert.True(decision.NewActivationAllowed);
        Assert.True(decision.DeviceTransferAllowed);
        Assert.True(decision.OfflineUseAllowed);
        Assert.False(decision.OfflineExpiryWarningRequired);
    }

    [Fact]
    public void FullSeatLimit_BlocksOnlyNewActivation()
    {
        var decision = CommercialLicensePolicy.Evaluate(
            Create(activeDeviceCount: 1), Now);

        Assert.True(decision.ExistingDeviceAllowed);
        Assert.False(decision.NewActivationAllowed);
    }

    [Fact]
    public void PaymentGrace_AllowsExistingDeviceButBlocksActivationAndTransfer()
    {
        var entitlement = Create(
            state: CommercialEntitlementState.Grace,
            paymentGraceEndsUtc: Now.AddDays(7));

        var decision = CommercialLicensePolicy.Evaluate(entitlement, Now);

        Assert.True(decision.ExistingDeviceAllowed);
        Assert.False(decision.NewActivationAllowed);
        Assert.False(decision.DeviceTransferAllowed);
    }

    [Fact]
    public void TransferCooldownAndAllowance_AreEnforced()
    {
        var cooldown = CommercialLicensePolicy.Evaluate(
            Create(lastTransferUtc: Now.AddDays(-6)), Now);
        var exhausted = CommercialLicensePolicy.Evaluate(
            Create(transfersUsed: 2), Now);
        var renewedWindow = CommercialLicensePolicy.Evaluate(
            Create(
                transfersUsed: 2,
                transferWindowStartedUtc: Now.AddDays(-365)), Now);

        Assert.False(cooldown.DeviceTransferAllowed);
        Assert.False(exhausted.DeviceTransferAllowed);
        Assert.True(renewedWindow.DeviceTransferAllowed);
    }

    [Fact]
    public void SameDeviceReinstall_DoesNotUseNewSeatOrTransfer()
    {
        var decision = CommercialLicensePolicy.Evaluate(
            Create(activeDeviceCount: 1, transfersUsed: 2), Now);

        Assert.True(CommercialLicensePolicy.IsSameDeviceReinstallAllowed(
            deviceWasPreviouslyRegistered: true,
            decision));
        Assert.False(CommercialLicensePolicy.IsSameDeviceReinstallAllowed(
            deviceWasPreviouslyRegistered: false,
            decision));
    }

    [Fact]
    public void OfflineWarning_BeginsDuringFinalSevenDays()
    {
        var decision = CommercialLicensePolicy.Evaluate(
            Create(offlineValidUntilUtc: Now.AddDays(7)), Now);

        Assert.True(decision.OfflineUseAllowed);
        Assert.True(decision.OfflineExpiryWarningRequired);
    }

    [Theory]
    [InlineData(CommercialEntitlementState.Pending)]
    [InlineData(CommercialEntitlementState.Expired)]
    [InlineData(CommercialEntitlementState.Suspended)]
    [InlineData(CommercialEntitlementState.Revoked)]
    [InlineData(CommercialEntitlementState.Refunded)]
    public void NonActiveStates_FailClosed(CommercialEntitlementState state)
    {
        var decision = CommercialLicensePolicy.Evaluate(
            Create(state: state), Now);

        Assert.False(decision.ExistingDeviceAllowed);
        Assert.False(decision.NewActivationAllowed);
        Assert.False(decision.DeviceTransferAllowed);
        Assert.False(decision.OfflineUseAllowed);
    }

    [Fact]
    public void UnknownOrMalformedValues_FailClosed()
    {
        var malformed = Create(state: (CommercialEntitlementState)999);

        Assert.False(CommercialLicensePolicy.Evaluate(null, Now)
            .ExistingDeviceAllowed);
        Assert.False(CommercialLicensePolicy.Evaluate(malformed, Now)
            .ExistingDeviceAllowed);
    }

    [Fact]
    public void NormalizedPaymentEvent_RequiresServerControlledFields()
    {
        var valid = new VerifiedPaymentEvent(
            "simulated", "event-1", "account-1", "pcspa-pro",
            "subscription-1", VerifiedPaymentEventKind.PurchaseCompleted,
            Now, Now.AddYears(1), 1, "INR", 49900);

        Assert.True(CommercialLicensePolicy.IsNormalizedPaymentEventValid(valid));
        Assert.False(CommercialLicensePolicy.IsNormalizedPaymentEventValid(
            valid with { ProviderEventId = "" }));
        Assert.False(CommercialLicensePolicy.IsNormalizedPaymentEventValid(
            valid with { SeatCount = 0 }));
        Assert.False(CommercialLicensePolicy.IsNormalizedPaymentEventValid(
            valid with { Currency = "inr" }));
        Assert.False(CommercialLicensePolicy.IsNormalizedPaymentEventValid(
            valid with { Kind = (VerifiedPaymentEventKind)999 }));
    }

    private static CommercialEntitlement Create(
        CommercialEntitlementState state = CommercialEntitlementState.Active,
        int activeDeviceCount = 0,
        DateTimeOffset? paymentGraceEndsUtc = null,
        DateTimeOffset? offlineValidUntilUtc = null,
        int transfersUsed = 0,
        DateTimeOffset? transferWindowStartedUtc = null,
        DateTimeOffset? lastTransferUtc = null) =>
        new(
            "entitlement-1",
            "account-1",
            "pcspa-pro",
            state,
            SeatLimit: 1,
            ActiveDeviceCount: activeDeviceCount,
            PeriodEndsUtc: Now.AddYears(1),
            PaymentGraceEndsUtc: paymentGraceEndsUtc,
            OfflineValidUntilUtc: offlineValidUntilUtc ?? Now.AddDays(30),
            TransfersUsedInRollingWindow: transfersUsed,
            TransferWindowStartedUtc:
                transferWindowStartedUtc ?? Now.AddDays(-30),
            LastTransferUtc: lastTransferUtc);
}
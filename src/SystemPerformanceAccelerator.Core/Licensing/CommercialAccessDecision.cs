namespace SystemPerformanceAccelerator.Core.Licensing;

public sealed record CommercialAccessDecision(
    bool ExistingDeviceAllowed,
    bool NewActivationAllowed,
    bool DeviceTransferAllowed,
    bool OfflineUseAllowed,
    bool OfflineExpiryWarningRequired,
    string Reason)
{
    public static CommercialAccessDecision Denied(string reason) =>
        new(false, false, false, false, false, reason);
}
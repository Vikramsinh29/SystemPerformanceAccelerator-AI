namespace SystemPerformanceAccelerator.Core.Models;

public enum PrivilegedOperationKind
{
    WindowsRepairRestoreHealth,
    WindowsRepairScanProtectedFiles,
    StartupManagerAllUsersStateChange
}

public sealed class PrivilegedOperationRequest
{
    private PrivilegedOperationRequest(
        PrivilegedOperationKind kind,
        StartupItem? startupItem = null,
        StartupItemState? requestedStartupState = null)
    {
        Kind = kind;
        StartupItem = startupItem;
        RequestedStartupState = requestedStartupState;
    }

    public PrivilegedOperationKind Kind { get; }

    public StartupItem? StartupItem { get; }

    public StartupItemState? RequestedStartupState { get; }

    public static PrivilegedOperationRequest CreateWindowsRepairRestoreHealth() =>
        new(PrivilegedOperationKind.WindowsRepairRestoreHealth);

    public static PrivilegedOperationRequest CreateWindowsRepairScanProtectedFiles() =>
        new(PrivilegedOperationKind.WindowsRepairScanProtectedFiles);

    public static PrivilegedOperationRequest CreateAllUsersStartupStateChange(
        StartupItem item,
        StartupItemState requestedState)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.SourceScope != StartupItemScope.AllUsers)
        {
            throw new ArgumentException(
                "Only an all-users startup item may cross the privileged-operation boundary.",
                nameof(item));
        }

        if (requestedState is not (
            StartupItemState.Enabled or StartupItemState.Disabled))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedState),
                requestedState,
                "Only Enabled or Disabled is a supported privileged startup state.");
        }

        var canApplyRequestedState = requestedState switch
        {
            StartupItemState.Enabled => item.CanEnable,
            StartupItemState.Disabled => item.CanDisable,
            _ => false
        };

        if (!canApplyRequestedState)
        {
            throw new ArgumentException(
                string.IsNullOrWhiteSpace(item.StateChangeUnavailableReason)
                    ? "The startup item is not eligible for the requested state change."
                    : item.StateChangeUnavailableReason,
                nameof(item));
        }

        return new PrivilegedOperationRequest(
            PrivilegedOperationKind.StartupManagerAllUsersStateChange,
            item,
            requestedState);
    }
}

public sealed record PrivilegedOperationResult(
    bool Started,
    bool Succeeded,
    string Code,
    string Message)
{
    public static PrivilegedOperationResult Rejected(
        string code,
        string message) =>
        new(false, false, code, message);
}

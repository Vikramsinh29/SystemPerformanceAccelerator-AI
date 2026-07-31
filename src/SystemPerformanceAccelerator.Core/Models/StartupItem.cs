namespace SystemPerformanceAccelerator.Core.Models;

public enum StartupItemState
{
    Enabled,
    Disabled,
    Unknown
}

public enum StartupTargetState
{
    Available,
    Missing,
    Unresolved,
    Malformed
}

public enum StartupItemKind
{
    Unknown,
    RegistryRun,
    StartupFolder
}

public enum StartupItemScope
{
    Unknown,
    CurrentUser,
    AllUsers
}

public enum StartupRegistryView
{
    NotApplicable,
    Registry32,
    Registry64
}

public sealed record StartupItem(
    string Name,
    string Command,
    string Source,
    string Location,
    StartupItemState State,
    StartupTargetState TargetState)
{
    public StartupItemKind Kind { get; init; } = StartupItemKind.Unknown;

    public StartupItemScope SourceScope { get; init; } = StartupItemScope.Unknown;

    public StartupRegistryView SourceRegistryView { get; init; } =
        StartupRegistryView.NotApplicable;

    public string EntryIdentifier { get; init; } = string.Empty;

    public StartupItemScope ApprovalScope { get; init; } = StartupItemScope.Unknown;

    public StartupRegistryView ApprovalRegistryView { get; init; } =
        StartupRegistryView.NotApplicable;

    public string ApprovalCategory { get; init; } = string.Empty;

    public long? SourceLengthBytes { get; init; }

    public DateTimeOffset? SourceLastWriteUtc { get; init; }

    public bool HasAmbiguousStateIdentity { get; init; }

    public bool CanDisable =>
        State == StartupItemState.Enabled &&
        HasSafeStateIdentity;

    public bool CanEnable =>
        State == StartupItemState.Disabled &&
        TargetState == StartupTargetState.Available &&
        HasSafeStateIdentity;

    public string StateChangeUnavailableReason
    {
        get
        {
            if (HasAmbiguousStateIdentity)
            {
                return "This row represents more than one physical startup entry and cannot be changed safely.";
            }

            if (!HasSafeStateIdentity)
            {
                return "Windows did not provide enough stable identity information to change this entry safely.";
            }

            if (State == StartupItemState.Unknown)
            {
                return "The current startup state is unknown. Run a fresh scan before making changes.";
            }

            if (State == StartupItemState.Disabled &&
                TargetState != StartupTargetState.Available)
            {
                return "The startup target is not currently available, so this entry cannot be enabled safely.";
            }

            return string.Empty;
        }
    }

    public string Status
    {
        get
        {
            var startupState = State switch
            {
                StartupItemState.Enabled => "Enabled",
                StartupItemState.Disabled => "Disabled",
                _ => "Unknown"
            };

            return TargetState switch
            {
                StartupTargetState.Available => startupState,
                StartupTargetState.Missing => $"{startupState} • Target missing",
                StartupTargetState.Unresolved => $"{startupState} • Target unresolved",
                StartupTargetState.Malformed => $"{startupState} • Malformed command",
                _ => startupState
            };
        }
    }

    private bool HasSafeStateIdentity =>
        !HasAmbiguousStateIdentity &&
        Kind != StartupItemKind.Unknown &&
        SourceScope != StartupItemScope.Unknown &&
        !string.IsNullOrWhiteSpace(EntryIdentifier) &&
        ApprovalScope != StartupItemScope.Unknown &&
        ApprovalRegistryView != StartupRegistryView.NotApplicable &&
        HasRecognizedStateLocation;

    private bool HasRecognizedStateLocation => Kind switch
    {
        StartupItemKind.RegistryRun =>
            SourceRegistryView != StartupRegistryView.NotApplicable &&
            ApprovalCategory is "Run" or "Run32",
        StartupItemKind.StartupFolder =>
            SourceRegistryView == StartupRegistryView.NotApplicable &&
            ApprovalCategory == "StartupFolder",
        _ => false
    };
}

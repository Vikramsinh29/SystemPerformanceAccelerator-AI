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

public sealed record StartupItem(
    string Name,
    string Command,
    string Source,
    string Location,
    StartupItemState State,
    StartupTargetState TargetState)
{
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
}

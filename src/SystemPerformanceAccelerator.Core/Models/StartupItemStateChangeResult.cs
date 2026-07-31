namespace SystemPerformanceAccelerator.Core.Models;

public enum StartupItemStateChangeOutcome
{
    Changed,
    NoChange,
    Stale,
    Unsupported,
    AccessDenied,
    Failed
}

public sealed record StartupItemStateChangeResult(
    StartupItemStateChangeOutcome Outcome,
    StartupItemState RequestedState,
    string Message)
{
    public bool Succeeded =>
        Outcome is StartupItemStateChangeOutcome.Changed or
            StartupItemStateChangeOutcome.NoChange;

    public bool StateChanged => Outcome == StartupItemStateChangeOutcome.Changed;
}

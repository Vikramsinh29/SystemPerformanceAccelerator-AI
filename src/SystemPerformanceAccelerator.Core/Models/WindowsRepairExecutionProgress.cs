namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairExecutionProgress(
    int CompletedSteps,
    int TotalSteps,
    WindowsRepairExecutionStep? CurrentStep,
    string Message)
{
    public int Percentage =>
        TotalSteps <= 0
            ? 0
            : Math.Clamp(
                (int)Math.Round(
                    CompletedSteps * 100d / TotalSteps),
                0,
                100);
}

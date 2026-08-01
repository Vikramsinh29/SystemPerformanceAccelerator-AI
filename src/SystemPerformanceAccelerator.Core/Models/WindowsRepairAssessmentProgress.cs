namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairAssessmentProgress(
    int CompletedChecks,
    int TotalChecks,
    WindowsRepairAssessmentCheck? CurrentCheck,
    string Message)
{
    public int Percentage =>
        TotalChecks <= 0
            ? 0
            : Math.Clamp(
                (int)Math.Round(
                    CompletedChecks * 100d / TotalChecks),
                0,
                100);
}

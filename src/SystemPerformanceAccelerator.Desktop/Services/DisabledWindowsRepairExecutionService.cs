using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class DisabledWindowsRepairExecutionService :
    IWindowsRepairExecutionService
{
    public WindowsRepairExecutionReadiness CheckReadiness(
        WindowsRepairAssessmentResult assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        return new WindowsRepairExecutionReadiness(
            "PLAN-UNAVAILABLE",
            assessment.ReferenceId,
            DateTimeOffset.UtcNow,
            IsReady: false,
            "Guided repair is unavailable",
            "The guided Windows repair service is unavailable in this context.",
            MinimumFreeBytes: 0,
            AvailableFreeBytes: null,
            new[]
            {
                "No repair command can start in this context."
            });
    }

    public Task<WindowsRepairExecutionResult> ExecuteAsync(
        WindowsRepairAssessmentResult assessment,
        Func<bool>? stopAfterCurrentStep = null,
        IProgress<WindowsRepairExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;

        return Task.FromResult(
            new WindowsRepairExecutionResult(
                "REPAIR-UNAVAILABLE",
                assessment.ReferenceId,
                now,
                now,
                assessment.ApplicationVersion,
                assessment.BuildIdentifier,
                WindowsRepairExecutionOutcome.Blocked,
                "The guided Windows repair service is unavailable.",
                Array.Empty<
                    WindowsRepairExecutionStepResult>(),
                VerificationAssessment: null,
                StopRequested: false,
                AutomaticRestartAttempted: false,
                new[]
                {
                    "No repair command was started."
                }));
    }
}

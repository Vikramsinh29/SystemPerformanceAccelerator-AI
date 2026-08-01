using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class DisabledWindowsRepairPlanService :
    IWindowsRepairPlanService
{
    public WindowsRepairPlan CreatePlan(
        WindowsRepairAssessmentResult assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        return new WindowsRepairPlan(
            "PLAN-DISABLED",
            assessment.ReferenceId,
            DateTimeOffset.UtcNow,
            assessment.ApplicationVersion,
            assessment.BuildIdentifier,
            WindowsRepairPlanDecision.Blocked,
            "Repair planning unavailable",
            "The guided repair planning service is unavailable in this application context.",
            Array.Empty<WindowsRepairPlanPreflightItem>(),
            Array.Empty<WindowsRepairPlanStep>(),
            RequiresFreshExecutionConsent: true,
            AuthorizesRepair: false,
            "No repair is authorized or executed.");
    }
}

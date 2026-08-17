using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed class OperationScopedWindowsRepairPlanService : IWindowsRepairPlanService
{
    private const string AdministratorPreflightTitle = "Administrator session";

    private readonly IWindowsRepairPlanService _inner;

    public OperationScopedWindowsRepairPlanService(IWindowsRepairPlanService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public WindowsRepairPlan CreatePlan(WindowsRepairAssessmentResult assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        var plan = _inner.CreatePlan(assessment);
        var preflight = plan.Preflight
            .Select(RewriteAdministratorPreflight)
            .ToArray();

        var hasBlocker = preflight.Any(item =>
            item.Status == WindowsRepairPlanItemStatus.Blocked);

        var assessmentNeedsAttention =
            assessment.OverallOutcome == WindowsRepairAssessmentOutcome.Attention ||
            assessment.Checks.Any(check =>
                check.Outcome == WindowsRepairAssessmentOutcome.Attention);

        var assessmentHealthy =
            assessment.OverallOutcome == WindowsRepairAssessmentOutcome.Healthy &&
            assessment.Checks.Count > 0 &&
            assessment.Checks.All(check =>
                check.Outcome == WindowsRepairAssessmentOutcome.Healthy);

        var decision = assessmentHealthy
            ? WindowsRepairPlanDecision.NotRecommended
            : assessmentNeedsAttention && !hasBlocker
                ? WindowsRepairPlanDecision.ReviewRequired
                : WindowsRepairPlanDecision.Blocked;

        var (title, summary) = decision switch
        {
            WindowsRepairPlanDecision.NotRecommended =>
                (
                    "Repair is not recommended",
                    "The latest read-only assessment did not classify an integrity problem. PC-SPA will not propose a Windows repair without evidence."
                ),
            WindowsRepairPlanDecision.ReviewRequired =>
                (
                    "Eligible for guided review",
                    "The latest assessment contains an Attention result and the current safety preflight has no blocker. Administrator permission will be requested only when a protected repair operation starts."
                ),
            _ =>
                (
                    "Repair planning is blocked",
                    "At least one safety condition is unresolved. Run a fresh assessment or resolve the blocked preflight item before considering guided repair."
                )
        };

        return plan with
        {
            Decision = decision,
            DecisionTitle = title,
            Summary = summary,
            Preflight = preflight,
            Disclosure =
                "This plan is read-only and does not itself authorize repair. Protected repair operations require fresh explicit confirmation and Windows UAC approval at execution time. PC-SPA will not restart Windows automatically."
        };
    }

    private static WindowsRepairPlanPreflightItem RewriteAdministratorPreflight(
        WindowsRepairPlanPreflightItem item)
    {
        if (!string.Equals(
                item.Title,
                AdministratorPreflightTitle,
                StringComparison.Ordinal))
        {
            return item;
        }

        return new WindowsRepairPlanPreflightItem(
            AdministratorPreflightTitle,
            WindowsRepairPlanItemStatus.Information,
            "PC-SPA runs normally without administrator rights. Windows UAC will request administrator permission only when a protected repair operation starts.");
    }
}

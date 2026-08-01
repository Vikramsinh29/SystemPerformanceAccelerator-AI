namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairPlan(
    string ReferenceId,
    string AssessmentReferenceId,
    DateTimeOffset CreatedUtc,
    string ApplicationVersion,
    string BuildIdentifier,
    WindowsRepairPlanDecision Decision,
    string DecisionTitle,
    string Summary,
    IReadOnlyList<WindowsRepairPlanPreflightItem> Preflight,
    IReadOnlyList<WindowsRepairPlanStep> Steps,
    bool RequiresFreshExecutionConsent,
    bool AuthorizesRepair,
    string Disclosure);

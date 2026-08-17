using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class OperationScopedWindowsRepairPlanServiceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-18T00:00:00Z");

    [Fact]
    public void NonElevatedAdministratorPreflight_DoesNotBlockOtherwiseSafePlan()
    {
        var assessment = CreateAttentionAssessment();
        var inner = new StubPlanService(CreatePlan(
            assessment,
            new WindowsRepairPlanPreflightItem(
                "Administrator session",
                WindowsRepairPlanItemStatus.Blocked,
                "Administrator permission is required before any future Windows repair.")));

        var service = new OperationScopedWindowsRepairPlanService(inner);
        var plan = service.CreatePlan(assessment);

        Assert.Equal(WindowsRepairPlanDecision.ReviewRequired, plan.Decision);
        var admin = Assert.Single(plan.Preflight.Where(item =>
            item.Title == "Administrator session"));
        Assert.Equal(WindowsRepairPlanItemStatus.Information, admin.Status);
        Assert.Contains("Windows UAC", admin.Detail, StringComparison.Ordinal);
        Assert.Contains("UAC", plan.Disclosure, StringComparison.Ordinal);
    }

    [Fact]
    public void UnrelatedBlockedPreflight_RemainsBlocked()
    {
        var assessment = CreateAttentionAssessment();
        var inner = new StubPlanService(CreatePlan(
            assessment,
            new WindowsRepairPlanPreflightItem(
                "Administrator session",
                WindowsRepairPlanItemStatus.Blocked,
                "Administrator permission is required before any future Windows repair."),
            new WindowsRepairPlanPreflightItem(
                "Pending restart state",
                WindowsRepairPlanItemStatus.Blocked,
                "Windows reports a pending restart.")));

        var service = new OperationScopedWindowsRepairPlanService(inner);
        var plan = service.CreatePlan(assessment);

        Assert.Equal(WindowsRepairPlanDecision.Blocked, plan.Decision);
        Assert.Contains(plan.Preflight, item =>
            item.Title == "Pending restart state" &&
            item.Status == WindowsRepairPlanItemStatus.Blocked);
    }

    private static WindowsRepairAssessmentResult CreateAttentionAssessment() =>
        new(
            "ASSESS-UAC",
            Now.AddMinutes(-2),
            Now.AddMinutes(-1),
            "1.0.0",
            "test-build",
            new WindowsRepairEnvironmentStatus(
                IsWindows: true,
                IsElevated: false,
                WindowsDescription: "Windows test",
                WindowsDirectory: @"C:\Windows",
                SystemDriveRoot: @"C:\",
                DismAvailable: true,
                SfcAvailable: true,
                SystemDriveFreeBytes: 20L * 1024 * 1024 * 1024,
                Issues: Array.Empty<string>()),
            new[]
            {
                new WindowsRepairCheckResult(
                    WindowsRepairAssessmentCheck.ComponentStoreCheckHealth,
                    WindowsRepairAssessmentOutcome.Attention,
                    "Windows component store",
                    "Review required.",
                    "DISM.exe",
                    Array.Empty<string>(),
                    0,
                    Now.AddMinutes(-2),
                    Now.AddMinutes(-1),
                    string.Empty,
                    string.Empty,
                    UserStopRequested: false,
                    "Test evidence.")
            },
            WindowsRepairAssessmentOutcome.Attention,
            StopRequested: false,
            Issues: Array.Empty<string>());

    private static WindowsRepairPlan CreatePlan(
        WindowsRepairAssessmentResult assessment,
        params WindowsRepairPlanPreflightItem[] preflight) =>
        new(
            "PLAN-UAC",
            assessment.ReferenceId,
            Now,
            assessment.ApplicationVersion,
            assessment.BuildIdentifier,
            WindowsRepairPlanDecision.Blocked,
            "Repair planning is blocked",
            "Blocked by legacy whole-app elevation requirement.",
            preflight,
            Array.Empty<WindowsRepairPlanStep>(),
            RequiresFreshExecutionConsent: true,
            AuthorizesRepair: false,
            "Legacy disclosure.");

    private sealed class StubPlanService(WindowsRepairPlan plan) : IWindowsRepairPlanService
    {
        public WindowsRepairPlan CreatePlan(WindowsRepairAssessmentResult assessment) => plan;
    }
}

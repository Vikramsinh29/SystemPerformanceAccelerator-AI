using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsRepairPlanServiceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-01T10:00:00Z");

    [Fact]
    public void HealthyAssessment_IsNotRecommended()
    {
        var service = CreateService(SafeRuntime());
        var plan = service.CreatePlan(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Healthy));

        Assert.Equal(
            WindowsRepairPlanDecision.NotRecommended,
            plan.Decision);
        Assert.False(plan.AuthorizesRepair);
        Assert.True(plan.RequiresFreshExecutionConsent);
        Assert.All(
            plan.Steps,
            step => Assert.False(step.IsProposed));
    }

    [Fact]
    public void AttentionAssessment_WithSafeRuntime_IsReviewRequired()
    {
        var service = CreateService(SafeRuntime());
        var plan = service.CreatePlan(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Attention));

        Assert.Equal(
            WindowsRepairPlanDecision.ReviewRequired,
            plan.Decision);
        Assert.Equal(4, plan.Steps.Count);
        Assert.All(
            plan.Steps,
            step => Assert.True(step.IsProposed));
        Assert.Contains(
            plan.Steps,
            step => step.ChangesWindows &&
                    step.MayUseWindowsUpdate);
        Assert.False(plan.AuthorizesRepair);
    }

    [Fact]
    public void InconclusiveAssessment_IsBlocked()
    {
        var service = CreateService(SafeRuntime());
        var plan = service.CreatePlan(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Inconclusive));

        Assert.Equal(
            WindowsRepairPlanDecision.Blocked,
            plan.Decision);
        Assert.Contains(
            plan.Preflight,
            item =>
                item.Title == "Assessment evidence" &&
                item.Status ==
                    WindowsRepairPlanItemStatus.Blocked);
    }

    [Fact]
    public void StaleAssessment_IsBlocked()
    {
        var service = CreateService(SafeRuntime());
        var plan = service.CreatePlan(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Attention,
                finishedUtc: Now.AddHours(-25)));

        Assert.Equal(
            WindowsRepairPlanDecision.Blocked,
            plan.Decision);
        Assert.Contains(
            plan.Preflight,
            item =>
                item.Title == "Latest assessment age" &&
                item.Status ==
                    WindowsRepairPlanItemStatus.Blocked);
    }

    [Fact]
    public void PendingRestart_IsBlocked()
    {
        var service = CreateService(
            SafeRuntime() with
            {
                PendingRestartDetected = true
            });
        var plan = service.CreatePlan(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Attention));

        Assert.Equal(
            WindowsRepairPlanDecision.Blocked,
            plan.Decision);
        Assert.Contains(
            plan.Preflight,
            item =>
                item.Title == "Pending restart state" &&
                item.Status ==
                    WindowsRepairPlanItemStatus.Blocked);
    }

    [Fact]
    public void UnknownPendingRestartState_IsBlocked()
    {
        var service = CreateService(
            SafeRuntime() with
            {
                PendingRestartDetected = null
            });
        var plan = service.CreatePlan(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Attention));

        Assert.Equal(
            WindowsRepairPlanDecision.Blocked,
            plan.Decision);
    }

    [Fact]
    public void NonElevatedRuntime_IsBlocked()
    {
        var service = CreateService(
            SafeRuntime() with
            {
                IsElevated = false
            });
        var plan = service.CreatePlan(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Attention));

        Assert.Equal(
            WindowsRepairPlanDecision.Blocked,
            plan.Decision);
        Assert.Contains(
            plan.Preflight,
            item =>
                item.Title == "Administrator session" &&
                item.Status ==
                    WindowsRepairPlanItemStatus.Blocked);
    }

    [Fact]
    public void RuntimeIssue_IsBlocked()
    {
        var service = CreateService(
            SafeRuntime() with
            {
                Issues = new[]
                {
                    "Preflight could not read a required state."
                }
            });
        var plan = service.CreatePlan(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Attention));

        Assert.Equal(
            WindowsRepairPlanDecision.Blocked,
            plan.Decision);
    }

    private static WindowsRepairPlanService CreateService(
        WindowsRepairPlanRuntimeStatus runtime) =>
        new(
            runtimeProvider: () => runtime,
            utcNow: () => Now,
            referenceFactory: () =>
                Guid.Parse(
                    "11111111-2222-3333-4444-555555555555"));

    private static WindowsRepairPlanRuntimeStatus SafeRuntime() =>
        new(
            IsWindows: true,
            IsElevated: true,
            DismAvailable: true,
            SfcAvailable: true,
            PendingRestartDetected: false,
            SystemDriveFreeBytes:
                20L * 1024 * 1024 * 1024,
            Issues: Array.Empty<string>());

    private static WindowsRepairAssessmentResult
        CreateAssessment(
            WindowsRepairAssessmentOutcome outcome,
            DateTimeOffset? finishedUtc = null)
    {
        var finished = finishedUtc ??
            Now.AddMinutes(-10);
        var check = new WindowsRepairCheckResult(
            WindowsRepairAssessmentCheck
                .ComponentStoreCheckHealth,
            outcome,
            "Windows component store",
            "Test assessment result.",
            "DISM.exe",
            Array.Empty<string>(),
            0,
            finished.AddSeconds(-1),
            finished,
            string.Empty,
            string.Empty,
            UserStopRequested: false,
            "Read-only test.");

        return new WindowsRepairAssessmentResult(
            "ASSESS-TEST",
            finished.AddSeconds(-1),
            finished,
            "1.0.0",
            "test-build",
            new WindowsRepairEnvironmentStatus(
                IsWindows: true,
                IsElevated: true,
                WindowsDescription: "Windows test",
                WindowsDirectory: @"C:\Windows",
                SystemDriveRoot: @"C:\",
                DismAvailable: true,
                SfcAvailable: true,
                SystemDriveFreeBytes:
                    20L * 1024 * 1024 * 1024,
                Issues: Array.Empty<string>()),
            new[] { check },
            outcome,
            StopRequested: false,
            Issues: Array.Empty<string>());
    }
}

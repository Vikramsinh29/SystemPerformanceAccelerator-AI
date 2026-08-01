using System.Reflection;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Services;
using SystemPerformanceAccelerator.Desktop.ViewModels;
using SystemPerformanceAccelerator.Infrastructure.Configuration;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsRepairAssessmentViewModelTests
{
    [Fact]
    public void Constructor_UsesSafeReadOnlyDefaults()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.CheckComponentStore);
        Assert.True(viewModel.VerifyProtectedSystemFiles);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.IsAssessmentRunning);
        Assert.False(viewModel.StopAfterCurrentRequested);
        Assert.Equal("Not assessed", viewModel.AssessmentState);
        Assert.Equal(
            "Ready for assessment",
            viewModel.CurrentCheckText);
        Assert.Equal(
            "Ready to run a read-only assessment",
            viewModel.ProgressText);
        Assert.Equal("Elapsed: --", viewModel.ElapsedText);
        Assert.Equal(
            "None",
            viewModel.TaskbarProgressState.ToString());
        Assert.True(
            viewModel.RunAssessmentCommand.CanExecute(null));
        Assert.False(
            viewModel.StopAfterCurrentCheckCommand.CanExecute(null));
    }

    [Fact]
    public void CompletionStatus_StopOnFinalCheck_DoesNotClaimAnythingWasSkipped()
    {
        var result = CreateResult(
            new[]
            {
                CreateCheck(
                    WindowsRepairAssessmentOutcome.Inconclusive)
            },
            stopRequested: true);

        var status = InvokeBuildCompletionStatus(result);

        Assert.Contains(
            "no additional selected checks remained",
            status,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "were skipped",
            status,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompletionStatus_StopWithSkippedCheck_ReportsSkippedCount()
    {
        var result = CreateResult(
            new[]
            {
                CreateCheck(
                    WindowsRepairAssessmentOutcome.Healthy),
                CreateCheck(
                    WindowsRepairAssessmentOutcome.Skipped)
            },
            stopRequested: true);

        var status = InvokeBuildCompletionStatus(result);

        Assert.Contains(
            "Skipped remaining selected checks: 1",
            status,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyProgress_ShowsCurrentCheckAndWorkingMessage()
    {
        var viewModel = CreateViewModel();
        var progress = new WindowsRepairAssessmentProgress(
            CompletedChecks: 0,
            TotalChecks: 1,
            CurrentCheck:
                WindowsRepairAssessmentCheck
                    .ProtectedSystemFilesVerifyOnly,
            Message:
                "Running protected Windows system-file verification. This is a read-only Microsoft Windows check.");

        InvokePrivateInstanceMethod(
            viewModel,
            "ApplyProgress",
            progress);

        Assert.Equal(
            "SFC VerifyOnly is running",
            viewModel.CurrentCheckText);
        Assert.Contains(
            "still working",
            viewModel.Status,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "keep this window open",
            viewModel.Status,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatElapsed_UsesReadableMinutesAndSeconds()
    {
        var method = typeof(WindowsRepairAssessmentViewModel)
            .GetMethod(
                "FormatElapsed",
                BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var value = Assert.IsType<string>(
            method.Invoke(
                null,
                new object[] { TimeSpan.FromSeconds(168) }));

        Assert.Equal("Elapsed: 2 min 48 sec", value);
    }

    [Fact]
    public void TaskbarProgressState_TracksAssessmentRunningOnly()
    {
        var viewModel = CreateViewModel();
        var property = typeof(WindowsRepairAssessmentViewModel)
            .GetProperty(
                nameof(
                    WindowsRepairAssessmentViewModel
                        .IsAssessmentRunning));
        var setter = property?.GetSetMethod(nonPublic: true);

        Assert.NotNull(setter);

        setter.Invoke(viewModel, new object[] { true });
        Assert.Equal(
            "Indeterminate",
            viewModel.TaskbarProgressState.ToString());
        Assert.True(
            viewModel.StopAfterCurrentCheckCommand.CanExecute(null));

        setter.Invoke(viewModel, new object[] { false });
        Assert.Equal(
            "None",
            viewModel.TaskbarProgressState.ToString());
        Assert.False(
            viewModel.StopAfterCurrentCheckCommand.CanExecute(null));
    }

    private static WindowsRepairAssessmentViewModel CreateViewModel()
    {
        var accessService = new FeatureAccessService(
            ApplicationEdition.Free,
            EditionFeatureEntitlements.Current);

        return new WindowsRepairAssessmentViewModel(
            new DisabledWindowsRepairAssessmentService(),
            new DisabledWindowsRepairAssessmentHistoryService(),
            new FeatureAccessGuard(accessService),
            new LocalDiagnosticService(),
            new NonInteractiveWindowsRepairAssessmentInteractionService());
    }

    private static string InvokeBuildCompletionStatus(
        WindowsRepairAssessmentResult result)
    {
        var method = typeof(WindowsRepairAssessmentViewModel)
            .GetMethod(
                "BuildCompletionStatus",
                BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<string>(
            method.Invoke(null, new object[] { result }));
    }

    private static void InvokePrivateInstanceMethod(
        WindowsRepairAssessmentViewModel viewModel,
        string methodName,
        object argument)
    {
        var method = typeof(WindowsRepairAssessmentViewModel)
            .GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
        method.Invoke(viewModel, new[] { argument });
    }

    private static WindowsRepairAssessmentResult CreateResult(
        IReadOnlyList<WindowsRepairCheckResult> checks,
        bool stopRequested)
    {
        var timestamp = DateTimeOffset.Parse(
            "2026-08-01T00:00:00Z");

        return new WindowsRepairAssessmentResult(
            "ASSESS-TEST",
            timestamp,
            timestamp.AddSeconds(1),
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
                    10L * 1024 * 1024 * 1024,
                Issues: Array.Empty<string>()),
            checks,
            WindowsRepairAssessmentOutcome.Inconclusive,
            stopRequested,
            Array.Empty<string>());
    }

    private static WindowsRepairCheckResult CreateCheck(
        WindowsRepairAssessmentOutcome outcome)
    {
        var timestamp = DateTimeOffset.Parse(
            "2026-08-01T00:00:00Z");

        return new WindowsRepairCheckResult(
            WindowsRepairAssessmentCheck.ComponentStoreCheckHealth,
            outcome,
            "Test check",
            "Test summary",
            "DISM.exe",
            Array.Empty<string>(),
            0,
            timestamp,
            timestamp.AddSeconds(1),
            string.Empty,
            string.Empty,
            outcome == WindowsRepairAssessmentOutcome.Skipped,
            "Read-only test.");
    }
}

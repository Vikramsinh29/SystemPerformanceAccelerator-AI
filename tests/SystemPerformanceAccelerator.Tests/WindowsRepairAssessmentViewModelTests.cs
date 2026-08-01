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
        Assert.False(viewModel.IsRepairPlanPreviewVisible);
        Assert.False(
            viewModel.PreviewRepairPlanCommand.CanExecute(null));
        Assert.False(
            viewModel.RunGuidedRepairCommand.CanExecute(null));
        Assert.False(viewModel.HasLatestRepairResult);
        Assert.False(
            viewModel.DeleteAssessmentHistoryCommand.CanExecute(null));
    }

    [Fact]
    public void Constructor_LoadsLatestGuidedRepairResult()
    {
        var result = CreateExecutionResult();
        var history = new TestExecutionHistoryService(result);

        var viewModel = CreateViewModel(
            executionHistoryService: history);

        Assert.True(viewModel.HasLatestRepairResult);
        Assert.True(viewModel.HasWindowsRepairHistory);
        Assert.Equal("Completed", viewModel.RepairResultOutcome);
        Assert.Equal("REPAIR-TEST", viewModel.RepairResultReference);
        Assert.Equal("Duration: 4 min 08 sec", viewModel.RepairResultDurationText);
        Assert.Equal("Succeeded", viewModel.DismRestoreHealthResult);
        Assert.Equal("Succeeded", viewModel.SfcScannowResult);
        Assert.Equal("Succeeded", viewModel.DismCheckHealthResult);
        Assert.Equal("Succeeded", viewModel.SfcVerifyOnlyResult);
        Assert.Contains(
            "did not automatically restart Windows",
            viewModel.AutomaticRestartEvidence,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(
            viewModel.DeleteAssessmentHistoryCommand.CanExecute(null));
    }

    [Fact]
    public void Constructor_MissingRepairStepIsReportedConservatively()
    {
        var result = CreateExecutionResult() with
        {
            Steps = CreateExecutionResult().Steps.Take(1).ToArray()
        };

        var viewModel = CreateViewModel(
            executionHistoryService:
                new TestExecutionHistoryService(result));

        Assert.Equal("Succeeded", viewModel.DismRestoreHealthResult);
        Assert.Equal("Not recorded", viewModel.SfcScannowResult);
        Assert.Equal("Not recorded", viewModel.DismCheckHealthResult);
        Assert.Equal("Not recorded", viewModel.SfcVerifyOnlyResult);
    }

    [Fact]
    public void DeleteHistory_WithExecutionOnly_ClearsDisplayedResult()
    {
        var history = new TestExecutionHistoryService(
            CreateExecutionResult());
        var viewModel = CreateViewModel(
            executionHistoryService: history,
            interactionService: new ConfirmingInteractionService());

        viewModel.DeleteAssessmentHistoryCommand.Execute(null);

        Assert.True(history.DeleteCalled);
        Assert.False(viewModel.HasLatestRepairResult);
        Assert.False(viewModel.HasWindowsRepairHistory);
        Assert.Equal("None", viewModel.RepairResultReference);
        Assert.False(
            viewModel.DeleteAssessmentHistoryCommand.CanExecute(null));
    }

    [Fact]
    public void ShowRepairResult_NewCompletionIsDisplayedImmediately()
    {
        var viewModel = CreateViewModel();
        var result = CreateExecutionResult() with
        {
            Outcome =
                WindowsRepairExecutionOutcome.CompletedWithAttention
        };

        InvokePrivateInstanceMethod(
            viewModel,
            "ShowRepairResult",
            result);

        Assert.True(viewModel.HasLatestRepairResult);
        Assert.Equal(
            "Completed with attention",
            viewModel.RepairResultOutcome);
        Assert.Contains(
            "Fresh verification still needs attention",
            viewModel.RepairResultRecommendation,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_AssessmentOnlyEnablesHistoryDeletion()
    {
        var assessment = CreateResult(
            new[]
            {
                CreateCheck(
                    WindowsRepairAssessmentOutcome.Healthy)
            },
            stopRequested: false);

        var viewModel = CreateViewModel(
            assessmentHistoryService:
                new TestAssessmentHistoryService(assessment));

        Assert.True(viewModel.HasLatestAssessment);
        Assert.False(viewModel.HasLatestRepairResult);
        Assert.True(viewModel.HasWindowsRepairHistory);
        Assert.True(
            viewModel.DeleteAssessmentHistoryCommand.CanExecute(null));
    }

    [Fact]
    public void Constructor_BothHistoryTypesRemainAvailable()
    {
        var assessment = CreateResult(
            new[]
            {
                CreateCheck(
                    WindowsRepairAssessmentOutcome.Healthy)
            },
            stopRequested: false);

        var viewModel = CreateViewModel(
            assessmentHistoryService:
                new TestAssessmentHistoryService(assessment),
            executionHistoryService:
                new TestExecutionHistoryService(
                    CreateExecutionResult()));

        Assert.True(viewModel.HasLatestAssessment);
        Assert.True(viewModel.HasLatestRepairResult);
        Assert.True(viewModel.HasWindowsRepairHistory);
        Assert.True(
            viewModel.DeleteAssessmentHistoryCommand.CanExecute(null));
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

    private static WindowsRepairAssessmentViewModel CreateViewModel(
        IWindowsRepairAssessmentHistoryService? assessmentHistoryService = null,
        IWindowsRepairExecutionHistoryService? executionHistoryService = null,
        IWindowsRepairAssessmentInteractionService? interactionService = null)
    {
        var accessService = new FeatureAccessService(
            ApplicationEdition.Free,
            EditionFeatureEntitlements.Current);

        return new WindowsRepairAssessmentViewModel(
            new DisabledWindowsRepairAssessmentService(),
            assessmentHistoryService ??
                new DisabledWindowsRepairAssessmentHistoryService(),
            new FeatureAccessGuard(accessService),
            new LocalDiagnosticService(),
            interactionService ??
                new NonInteractiveWindowsRepairAssessmentInteractionService(),
            repairExecutionHistoryService: executionHistoryService);
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

    private static WindowsRepairExecutionResult CreateExecutionResult()
    {
        var started = DateTimeOffset.Parse(
            "2026-08-01T12:00:00Z");
        var steps = Enum
            .GetValues<WindowsRepairExecutionStep>()
            .Select((step, index) =>
                new WindowsRepairExecutionStepResult(
                    step,
                    WindowsRepairExecutionStepOutcome.Succeeded,
                    step.ToString(),
                    "Completed.",
                    ChangesWindows: index < 2,
                    index % 2 == 0 ? "DISM.exe" : "sfc.exe",
                    Array.Empty<string>(),
                    0,
                    started.AddMinutes(index),
                    started.AddMinutes(index + 1),
                    string.Empty,
                    string.Empty))
            .ToArray();

        return new WindowsRepairExecutionResult(
            "REPAIR-TEST",
            "ASSESS-TEST",
            started,
            started.AddMinutes(4).AddSeconds(8),
            "1.0.0",
            "test-build",
            WindowsRepairExecutionOutcome.Completed,
            "Completed.",
            steps,
            VerificationAssessment: null,
            StopRequested: false,
            AutomaticRestartAttempted: false,
            Issues: Array.Empty<string>());
    }

    private sealed class TestExecutionHistoryService(
        WindowsRepairExecutionResult? latest) :
        IWindowsRepairExecutionHistoryService
    {
        public string ExecutionRoot => string.Empty;

        public bool DeleteCalled { get; private set; }

        public Task SaveAsync(
            WindowsRepairExecutionResult result,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public WindowsRepairExecutionResult? LoadLatest() => latest;

        public void DeleteHistory() => DeleteCalled = true;
    }

    private sealed class TestAssessmentHistoryService(
        WindowsRepairAssessmentResult? latest) :
        IWindowsRepairAssessmentHistoryService
    {
        public string AssessmentRoot => string.Empty;

        public Task SaveAsync(
            WindowsRepairAssessmentResult result,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public WindowsRepairAssessmentResult? LoadLatest() => latest;

        public Task<string?> ExportLatestAsync(
            string destinationZipPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public void DeleteHistory()
        {
        }
    }

    private sealed class ConfirmingInteractionService :
        IWindowsRepairAssessmentInteractionService
    {
        public bool ConfirmAssessment(
            WindowsRepairAssessmentRequest request) => true;

        public string? ChooseReportDestination(
            string suggestedFileName) => null;

        public bool ConfirmDeleteHistory() => true;

        public void OpenFolder(string path)
        {
        }

        public void ShowMessage(
            string title,
            string message,
            bool isError = false)
        {
        }
    }
}

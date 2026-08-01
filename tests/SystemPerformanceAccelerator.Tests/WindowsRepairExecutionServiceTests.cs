using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsRepairExecutionServiceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse(
            "2026-08-01T12:00:00Z");

    [Fact]
    public void HealthyAssessment_IsBlocked()
    {
        var service = CreateService(
            new RecordingRunner(),
            CreateVerification(
                WindowsRepairAssessmentOutcome.Healthy));

        var readiness = service.CheckReadiness(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Healthy));

        Assert.False(readiness.IsReady);
        Assert.Contains(
            readiness.Issues,
            item => item.Contains(
                "not classify",
                StringComparison.OrdinalIgnoreCase) ||
                item.Contains(
                    "not justified",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AttentionAssessment_WithSafePreflight_IsReady()
    {
        var service = CreateService(
            new RecordingRunner(),
            CreateVerification(
                WindowsRepairAssessmentOutcome.Healthy));

        var readiness = service.CheckReadiness(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Attention));

        Assert.True(readiness.IsReady);
        Assert.Empty(readiness.Issues);
        Assert.Equal(
            WindowsRepairExecutionService
                .MinimumRepairFreeBytes,
            readiness.MinimumFreeBytes);
    }

    [Fact]
    public void LowFreeSpace_IsBlocked()
    {
        var service = CreateService(
            new RecordingRunner(),
            CreateVerification(
                WindowsRepairAssessmentOutcome.Healthy),
            freeBytes:
                WindowsRepairExecutionService
                    .MinimumRepairFreeBytes - 1);

        var readiness = service.CheckReadiness(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Attention));

        Assert.False(readiness.IsReady);
        Assert.Contains(
            readiness.Issues,
            item => item.Contains(
                "at least",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_RunsDismThenSfcThenVerification()
    {
        var runner = new RecordingRunner();
        var verification =
            CreateVerification(
                WindowsRepairAssessmentOutcome.Healthy);
        var assessmentService =
            new RecordingAssessmentService(
                verification);
        var service = CreateService(
            runner,
            assessmentService);

        var result = await service.ExecuteAsync(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Attention));

        Assert.Equal(
            WindowsRepairExecutionOutcome.Completed,
            result.Outcome);
        Assert.Equal(
            new[]
            {
                WindowsRepairExecutionStep
                    .ComponentStoreRepair,
                WindowsRepairExecutionStep
                    .ProtectedSystemFilesRepair
            },
            runner.Requests.Select(item =>
                item.Step).ToArray());
        Assert.Equal(1,
            assessmentService.CallCount);
        Assert.Equal(4, result.Steps.Count);
        Assert.NotNull(
            result.VerificationAssessment);
        Assert.False(
            result.AutomaticRestartAttempted);
    }

    [Fact]
    public async Task ExecuteAsync_DismFailureStopsBeforeSfc()
    {
        var runner = new RecordingRunner(
            new WindowsRepairExecutionCommandResult(
                Started: true,
                ExitCode: 87,
                Now,
                Now.AddSeconds(1),
                string.Empty,
                "DISM failed.",
                string.Empty));
        var assessmentService =
            new RecordingAssessmentService(
                CreateVerification(
                    WindowsRepairAssessmentOutcome.Healthy));
        var service = CreateService(
            runner,
            assessmentService);

        var result = await service.ExecuteAsync(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Attention));

        Assert.Equal(
            WindowsRepairExecutionOutcome.Failed,
            result.Outcome);
        Assert.Single(runner.Requests);
        Assert.Equal(0,
            assessmentService.CallCount);
        Assert.Equal(4, result.Steps.Count);
        Assert.Equal(
            WindowsRepairExecutionStepOutcome.Failed,
            result.Steps[0].Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_StopAfterDismSkipsRemainingSteps()
    {
        var runner = new RecordingRunner();
        var assessmentService =
            new RecordingAssessmentService(
                CreateVerification(
                    WindowsRepairAssessmentOutcome.Healthy));
        var service = CreateService(
            runner,
            assessmentService);

        var result = await service.ExecuteAsync(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Attention),
            stopAfterCurrentStep: () => true);

        Assert.Equal(
            WindowsRepairExecutionOutcome.Stopped,
            result.Outcome);
        Assert.True(result.StopRequested);
        Assert.Single(runner.Requests);
        Assert.Equal(0,
            assessmentService.CallCount);
        Assert.Equal(4, result.Steps.Count);
        Assert.Equal(
            3,
            result.Steps.Count(item =>
                item.Outcome ==
                WindowsRepairExecutionStepOutcome.Skipped));
    }

    [Fact]
    public async Task ExecuteAsync_VerificationAttentionIsNotClaimedHealthy()
    {
        var service = CreateService(
            new RecordingRunner(),
            CreateVerification(
                WindowsRepairAssessmentOutcome.Attention));

        var result = await service.ExecuteAsync(
            CreateAssessment(
                WindowsRepairAssessmentOutcome.Attention));

        Assert.Equal(
            WindowsRepairExecutionOutcome
                .CompletedWithAttention,
            result.Outcome);
        Assert.Contains(
            "requires review",
            result.Summary,
            StringComparison.OrdinalIgnoreCase);
    }

    private static WindowsRepairExecutionService CreateService(
        RecordingRunner runner,
        WindowsRepairAssessmentResult verification,
        long? freeBytes =
            20L * 1024 * 1024 * 1024) =>
        CreateService(
            runner,
            new RecordingAssessmentService(
                verification),
            freeBytes);

    private static WindowsRepairExecutionService CreateService(
        RecordingRunner runner,
        RecordingAssessmentService assessmentService,
        long? freeBytes =
            20L * 1024 * 1024 * 1024)
    {
        var planService =
            new WindowsRepairPlanService(
                runtimeProvider: () =>
                    new WindowsRepairPlanRuntimeStatus(
                        IsWindows: true,
                        IsElevated: true,
                        DismAvailable: true,
                        SfcAvailable: true,
                        PendingRestartDetected: false,
                        SystemDriveFreeBytes:
                            20L * 1024 * 1024 * 1024,
                        Issues:
                            Array.Empty<string>()),
                utcNow: () => Now,
                referenceFactory: () =>
                    Guid.Parse(
                        "11111111-2222-3333-4444-555555555555"));

        return new WindowsRepairExecutionService(
            runner,
            assessmentService,
            planService,
            freeSpaceProvider: () => freeBytes,
            utcNow: () => Now,
            referenceFactory: () =>
                Guid.Parse(
                    "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"));
    }

    private static WindowsRepairAssessmentResult
        CreateAssessment(
            WindowsRepairAssessmentOutcome outcome)
    {
        var finished = Now.AddMinutes(-5);

        return new WindowsRepairAssessmentResult(
            "ASSESS-INPUT",
            finished.AddSeconds(-1),
            finished,
            "1.0.0",
            "test-build",
            SafeEnvironment(),
            new[]
            {
                CreateCheck(
                    WindowsRepairAssessmentCheck
                        .ComponentStoreCheckHealth,
                    outcome,
                    finished)
            },
            outcome,
            StopRequested: false,
            Issues: Array.Empty<string>());
    }

    private static WindowsRepairAssessmentResult
        CreateVerification(
            WindowsRepairAssessmentOutcome outcome)
    {
        var finished = Now.AddMinutes(1);
        var checkOutcome = outcome ==
                WindowsRepairAssessmentOutcome.Healthy
            ? WindowsRepairAssessmentOutcome.Healthy
            : WindowsRepairAssessmentOutcome.Attention;

        return new WindowsRepairAssessmentResult(
            "ASSESS-VERIFY",
            finished.AddSeconds(-2),
            finished,
            "1.0.0",
            "test-build",
            SafeEnvironment(),
            new[]
            {
                CreateCheck(
                    WindowsRepairAssessmentCheck
                        .ComponentStoreCheckHealth,
                    checkOutcome,
                    finished.AddSeconds(-1)),
                CreateCheck(
                    WindowsRepairAssessmentCheck
                        .ProtectedSystemFilesVerifyOnly,
                    checkOutcome,
                    finished)
            },
            outcome,
            StopRequested: false,
            Issues: Array.Empty<string>());
    }

    private static WindowsRepairCheckResult CreateCheck(
        WindowsRepairAssessmentCheck check,
        WindowsRepairAssessmentOutcome outcome,
        DateTimeOffset finished) =>
        new(
            check,
            outcome,
            check ==
                WindowsRepairAssessmentCheck
                    .ComponentStoreCheckHealth
                ? "Windows component store"
                : "Protected Windows files",
            outcome ==
                WindowsRepairAssessmentOutcome.Healthy
                ? "No classified issue."
                : "Review required.",
            check ==
                WindowsRepairAssessmentCheck
                    .ComponentStoreCheckHealth
                ? "DISM.exe"
                : "sfc.exe",
            Array.Empty<string>(),
            0,
            finished.AddSeconds(-1),
            finished,
            string.Empty,
            string.Empty,
            UserStopRequested: false,
            "Test verification.");

    private static WindowsRepairEnvironmentStatus
        SafeEnvironment() =>
        new(
            IsWindows: true,
            IsElevated: true,
            WindowsDescription: "Windows test",
            WindowsDirectory: @"C:\Windows",
            SystemDriveRoot: @"C:\",
            DismAvailable: true,
            SfcAvailable: true,
            SystemDriveFreeBytes:
                20L * 1024 * 1024 * 1024,
            Issues: Array.Empty<string>());

    private sealed class RecordingRunner :
        IWindowsRepairExecutionCommandRunner
    {
        private readonly Queue<
            WindowsRepairExecutionCommandResult>
            _results = new();

        public RecordingRunner(
            params WindowsRepairExecutionCommandResult[]
                results)
        {
            foreach (var result in results)
            {
                _results.Enqueue(result);
            }
        }

        public List<
            WindowsRepairExecutionCommandRequest>
            Requests { get; } = [];

        public Task<WindowsRepairExecutionCommandResult>
            RunAsync(
                WindowsRepairExecutionCommandRequest request,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);

            if (_results.Count > 0)
            {
                return Task.FromResult(
                    _results.Dequeue());
            }

            return Task.FromResult(
                new WindowsRepairExecutionCommandResult(
                    Started: true,
                    ExitCode: 0,
                    Now,
                    Now.AddSeconds(1),
                    "Completed successfully.",
                    string.Empty,
                    string.Empty));
        }
    }

    private sealed class RecordingAssessmentService :
        IWindowsRepairAssessmentService
    {
        private readonly WindowsRepairAssessmentResult
            _result;

        public RecordingAssessmentService(
            WindowsRepairAssessmentResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<WindowsRepairAssessmentResult>
            AssessAsync(
                WindowsRepairAssessmentRequest request,
                Func<bool>? stopAfterCurrentCheck = null,
                IProgress<WindowsRepairAssessmentProgress>?
                    progress = null,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_result);
        }
    }
}

using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsRepairAssessmentServiceTests
{
    [Fact]
    public async Task AssessAsync_WhenNothingSelected_DoesNotRunCommands()
    {
        var runner = new FakeRunner();
        var service = CreateService(runner);

        var result = await service.AssessAsync(
            new WindowsRepairAssessmentRequest(false, false));

        Assert.Equal(
            WindowsRepairAssessmentOutcome.Unsupported,
            result.OverallOutcome);
        Assert.Empty(result.Checks);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task AssessAsync_WhenDesktopIsNotElevated_UsesConfiguredRunner()
    {
        var runner = new FakeRunner(
            Completed(
                "No component store corruption detected."));

        var service = CreateService(
            runner,
            CreateEnvironment(isElevated: false));

        var result = await service.AssessAsync(
            new WindowsRepairAssessmentRequest(
                true,
                false));

        Assert.Equal(
            1,
            runner.CallCount);

        Assert.Single(
            result.Checks);

        Assert.Equal(
            WindowsRepairAssessmentOutcome.Healthy,
            result.Checks[0].Outcome);

        Assert.Equal(
            WindowsRepairAssessmentOutcome.Healthy,
            result.OverallOutcome);
    }

    [Fact]
    public async Task AssessAsync_RunsSelectedChecksInSafeOrder()
    {
        var runner = new FakeRunner(
            Completed(
                "No component store corruption detected."),
            Completed(
                "Windows Resource Protection did not find any integrity violations."));
        var service = CreateService(runner);

        var result = await service.AssessAsync(
            WindowsRepairAssessmentRequest.Default);

        Assert.Equal(2, runner.CallCount);
        Assert.Equal(
            new[]
            {
                WindowsRepairAssessmentCheck.ComponentStoreCheckHealth,
                WindowsRepairAssessmentCheck
                    .ProtectedSystemFilesVerifyOnly
            },
            runner.Requests.Select(item => item.Check).ToArray());
        Assert.Equal(
            WindowsRepairAssessmentOutcome.Healthy,
            result.OverallOutcome);
    }

    [Fact]
    public async Task AssessAsync_StopRequest_SkipsRemainingCheck()
    {
        var runner = new FakeRunner(
            Completed(
                "No component store corruption detected."));
        var service = CreateService(runner);

        var result = await service.AssessAsync(
            WindowsRepairAssessmentRequest.Default,
            stopAfterCurrentCheck: () => runner.CallCount >= 1);

        Assert.True(result.StopRequested);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal(2, result.Checks.Count);
        Assert.Equal(
            WindowsRepairAssessmentOutcome.Skipped,
            result.Checks[1].Outcome);
        Assert.True(result.Checks[1].UserStopRequested);
    }

    [Fact]
    public async Task AssessAsync_StopRequestOnFinalCheck_ReportsNoRemainingChecks()
    {
        var runner = new FakeRunner(
            Completed(
                "Windows Resource Protection did not find any integrity violations."));
        var service = CreateService(runner);
        var messages = new List<string>();
        var progress =
            new ImmediateProgress<WindowsRepairAssessmentProgress>(
                item => messages.Add(item.Message));

        var result = await service.AssessAsync(
            new WindowsRepairAssessmentRequest(false, true),
            stopAfterCurrentCheck: () => runner.CallCount >= 1,
            progress: progress);

        Assert.True(result.StopRequested);
        Assert.Single(result.Checks);
        Assert.DoesNotContain(
            result.Checks,
            item => item.Outcome ==
                WindowsRepairAssessmentOutcome.Skipped);
        Assert.Contains(
            messages,
            message => message.Contains(
                "No additional selected checks remained.",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task AssessAsync_NonZeroExit_IsFailedNotCorruptionClaim()
    {
        var runner = new FakeRunner(
            Completed("Access problem", exitCode: 5));
        var service = CreateService(runner);

        var result = await service.AssessAsync(
            new WindowsRepairAssessmentRequest(true, false));

        Assert.Equal(
            WindowsRepairAssessmentOutcome.Failed,
            result.OverallOutcome);
        Assert.Contains(
            "exit code 5",
            result.Checks[0].Summary,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretText_DismNoCorruption_IsHealthy()
    {
        var outcome =
            WindowsRepairAssessmentService.InterpretText(
                WindowsRepairAssessmentCheck.ComponentStoreCheckHealth,
                "No component store corruption detected.");

        Assert.Equal(
            WindowsRepairAssessmentOutcome.Healthy,
            outcome);
    }

    [Fact]
    public void InterpretText_SfcIntegrityViolation_IsAttention()
    {
        var outcome =
            WindowsRepairAssessmentService.InterpretText(
                WindowsRepairAssessmentCheck
                    .ProtectedSystemFilesVerifyOnly,
                "Windows Resource Protection found integrity violations.");

        Assert.Equal(
            WindowsRepairAssessmentOutcome.Attention,
            outcome);
    }

    [Fact]
    public async Task AssessAsync_RealSfcOutputWithNullSeparators_IsNormalizedAndHealthy()
    {
        var capturedOutput = string.Join(
            "\0",
            "Windows Resource Protection did not find any integrity violations."
                .ToCharArray()) + "\0";
        var runner = new FakeRunner(Completed(capturedOutput));
        var service = CreateService(runner);

        var result = await service.AssessAsync(
            new WindowsRepairAssessmentRequest(false, true));

        var check = Assert.Single(result.Checks);
        Assert.Equal(
            WindowsRepairAssessmentOutcome.Healthy,
            check.Outcome);
        Assert.False(check.SanitizedOutput.Contains('\0'));
        Assert.Contains(
            "did not find any integrity violations",
            check.SanitizedOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretText_UnknownOrLocalizedText_IsInconclusive()
    {
        var outcome =
            WindowsRepairAssessmentService.InterpretText(
                WindowsRepairAssessmentCheck
                    .ProtectedSystemFilesVerifyOnly,
                "Localized output not recognized.");

        Assert.Equal(
            WindowsRepairAssessmentOutcome.Inconclusive,
            outcome);
    }

    private static WindowsRepairAssessmentService CreateService(
        FakeRunner runner,
        WindowsRepairEnvironmentStatus? environment = null)
    {
        var timestamp = DateTimeOffset.Parse(
            "2026-08-01T00:00:00Z");

        return new WindowsRepairAssessmentService(
            runner,
            environmentProvider: () =>
                environment ?? CreateEnvironment(),
            utcNow: () => timestamp,
            versionProvider: () => ("1.0.0", "test-build"));
    }

    private static WindowsRepairEnvironmentStatus CreateEnvironment(
        bool isElevated = true) =>
        new(
            IsWindows: true,
            IsElevated: isElevated,
            WindowsDescription: "Windows test",
            WindowsDirectory: @"C:\Windows",
            SystemDriveRoot: @"C:\",
            DismAvailable: true,
            SfcAvailable: true,
            SystemDriveFreeBytes: 10L * 1024 * 1024 * 1024,
            Issues: Array.Empty<string>());

    private static WindowsRepairCommandResult Completed(
        string output,
        int exitCode = 0)
    {
        var start = DateTimeOffset.Parse(
            "2026-08-01T00:00:00Z");

        return new WindowsRepairCommandResult(
            Started: true,
            exitCode,
            start,
            start.AddSeconds(1),
            output,
            string.Empty,
            string.Empty);
    }

    private sealed class ImmediateProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public ImmediateProgress(Action<T> report)
        {
            _report = report;
        }

        public void Report(T value) => _report(value);
    }

    private sealed class FakeRunner :
        IWindowsRepairCommandRunner
    {
        private readonly Queue<WindowsRepairCommandResult>
            _results;

        public FakeRunner(
            params WindowsRepairCommandResult[] results)
        {
            _results = new Queue<WindowsRepairCommandResult>(
                results);
        }

        public List<WindowsRepairCommandRequest> Requests { get; } =
            [];

        public int CallCount => Requests.Count;

        public Task<WindowsRepairCommandResult> RunAsync(
            WindowsRepairCommandRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);

            if (_results.Count == 0)
            {
                throw new InvalidOperationException(
                    "No fake command result was configured.");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }
}

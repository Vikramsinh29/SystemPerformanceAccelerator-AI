using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed class WindowsRepairExecutionService :
    IWindowsRepairExecutionService
{
    public const long MinimumRepairFreeBytes =
        5L * 1024 * 1024 * 1024;

    private const int TotalSteps = 4;

    private readonly IWindowsRepairExecutionCommandRunner
        _commandRunner;
    private readonly IWindowsRepairAssessmentService
        _assessmentService;
    private readonly IWindowsRepairPlanService
        _planService;
    private readonly DiagnosticPathSanitizer _sanitizer;
    private readonly Func<long?> _freeSpaceProvider;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<Guid> _referenceFactory;

    public WindowsRepairExecutionService(
        IWindowsRepairExecutionCommandRunner commandRunner,
        IWindowsRepairAssessmentService assessmentService,
        IWindowsRepairPlanService planService,
        DiagnosticPathSanitizer? sanitizer = null,
        Func<long?>? freeSpaceProvider = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<Guid>? referenceFactory = null)
    {
        _commandRunner = commandRunner ??
            throw new ArgumentNullException(
                nameof(commandRunner));
        _assessmentService = assessmentService ??
            throw new ArgumentNullException(
                nameof(assessmentService));
        _planService = planService ??
            throw new ArgumentNullException(
                nameof(planService));
        _sanitizer = sanitizer ??
            new DiagnosticPathSanitizer();
        _freeSpaceProvider = freeSpaceProvider ??
            ReadWindowsDriveFreeSpace;
        _utcNow = utcNow ??
            (() => DateTimeOffset.UtcNow);
        _referenceFactory = referenceFactory ??
            Guid.NewGuid;
    }

    public WindowsRepairExecutionReadiness CheckReadiness(
        WindowsRepairAssessmentResult assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        var evaluatedUtc =
            _utcNow().ToUniversalTime();
        var plan = _planService.CreatePlan(assessment);
        var availableFreeBytes =
            _freeSpaceProvider();
        var issues = plan.Preflight
            .Where(item =>
                item.Status ==
                WindowsRepairPlanItemStatus.Blocked)
            .Select(item => item.Detail)
            .Where(item =>
                !string.IsNullOrWhiteSpace(item))
            .Select(_sanitizer.Sanitize)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (plan.Decision !=
                WindowsRepairPlanDecision.ReviewRequired &&
            issues.Count == 0)
        {
            issues.Add(
                _sanitizer.Sanitize(plan.Summary));
        }

        if (!availableFreeBytes.HasValue)
        {
            issues.Add(
                "Available Windows-drive space could not be read at execution time.");
        }
        else if (availableFreeBytes.Value <
                 MinimumRepairFreeBytes)
        {
            issues.Add(
                $"PC-SPA requires at least {FormatBytes(MinimumRepairFreeBytes)} free on the Windows drive before guided repair.");
        }

        var isReady =
            plan.Decision ==
                WindowsRepairPlanDecision.ReviewRequired &&
            issues.Count == 0;

        return new WindowsRepairExecutionReadiness(
            plan.ReferenceId,
            assessment.ReferenceId,
            evaluatedUtc,
            isReady,
            isReady
                ? "Ready for explicit guided-repair confirmation"
                : "Guided repair is blocked",
            isReady
                ? "Fresh execution-time safety checks passed. The repair still requires explicit confirmation and will never restart Windows automatically."
                : "One or more execution-time safety conditions did not pass. No repair command can start.",
            MinimumRepairFreeBytes,
            availableFreeBytes,
            issues.ToArray());
    }

    public async Task<WindowsRepairExecutionResult> ExecuteAsync(
        WindowsRepairAssessmentResult assessment,
        Func<bool>? stopAfterCurrentStep = null,
        IProgress<WindowsRepairExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc =
            _utcNow().ToUniversalTime();
        var referenceId =
            CreateReferenceId(startedUtc);
        var readiness = CheckReadiness(assessment);
        var steps = new List<
            WindowsRepairExecutionStepResult>();
        var issues = readiness.Issues.ToList();

        if (!readiness.IsReady)
        {
            return Complete(
                WindowsRepairExecutionOutcome.Blocked,
                "Guided repair was blocked by fresh execution-time safety checks.",
                verification: null,
                stopRequested: false);
        }

        progress?.Report(
            new WindowsRepairExecutionProgress(
                0,
                TotalSteps,
                null,
                "Fresh execution-time preflight passed. Preparing the Microsoft component-store repair."));

        var windowsDirectory =
            assessment.Environment.WindowsDirectory;

        var dismRequest =
            WindowsRepairExecutionCommandRequest
                .CreateDismRestoreHealth(
                    windowsDirectory);
        progress?.Report(
            new WindowsRepairExecutionProgress(
                0,
                TotalSteps,
                dismRequest.Step,
                "Running DISM RestoreHealth. Microsoft servicing may use Windows Update."));

        var dismCommand = await _commandRunner
            .RunAsync(
                dismRequest,
                cancellationToken)
            .ConfigureAwait(false);
        var dismStep = InterpretRepairCommand(
            dismRequest,
            dismCommand);
        steps.Add(dismStep);
        progress?.Report(
            new WindowsRepairExecutionProgress(
                1,
                TotalSteps,
                dismRequest.Step,
                "DISM RestoreHealth finished."));

        if (dismStep.Outcome ==
            WindowsRepairExecutionStepOutcome.Failed)
        {
            AddSkippedRemaining(
                steps,
                1,
                "Skipped because DISM RestoreHealth did not complete successfully.");
            return Complete(
                WindowsRepairExecutionOutcome.Failed,
                "The component-store repair did not complete successfully. PC-SPA stopped before SFC repair or verification.",
                verification: null,
                stopRequested: false);
        }

        if (stopAfterCurrentStep?.Invoke() ?? false)
        {
            AddSkippedRemaining(
                steps,
                1,
                "Skipped because the user requested Stop after current step.");
            return Complete(
                WindowsRepairExecutionOutcome.Stopped,
                "Guided repair stopped after DISM finished normally. Remaining steps were not started.",
                verification: null,
                stopRequested: true);
        }

        var sfcRequest =
            WindowsRepairExecutionCommandRequest
                .CreateSfcScanNow(windowsDirectory);
        progress?.Report(
            new WindowsRepairExecutionProgress(
                1,
                TotalSteps,
                sfcRequest.Step,
                "Running SFC Scannow to repair protected Windows files."));

        var sfcCommand = await _commandRunner
            .RunAsync(
                sfcRequest,
                cancellationToken)
            .ConfigureAwait(false);
        var sfcStep = InterpretRepairCommand(
            sfcRequest,
            sfcCommand);
        steps.Add(sfcStep);
        progress?.Report(
            new WindowsRepairExecutionProgress(
                2,
                TotalSteps,
                sfcRequest.Step,
                "SFC Scannow finished."));

        if (sfcStep.Outcome ==
            WindowsRepairExecutionStepOutcome.Failed)
        {
            AddSkippedRemaining(
                steps,
                2,
                "Skipped because SFC Scannow did not complete successfully.");
            return Complete(
                WindowsRepairExecutionOutcome.Failed,
                "The protected-file repair did not complete successfully. PC-SPA stopped before verification.",
                verification: null,
                stopRequested: false);
        }

        if (stopAfterCurrentStep?.Invoke() ?? false)
        {
            AddSkippedRemaining(
                steps,
                2,
                "Skipped because the user requested Stop after current step.");
            return Complete(
                WindowsRepairExecutionOutcome.Stopped,
                "Guided repair stopped after SFC finished normally. Verification steps were not started.",
                verification: null,
                stopRequested: true);
        }

        var verificationProgress =
            new Progress<WindowsRepairAssessmentProgress>(
                value =>
                {
                    var mappedStep =
                        MapVerificationStep(
                            value.CurrentCheck);
                    progress?.Report(
                        new WindowsRepairExecutionProgress(
                            2 + Math.Clamp(
                                value.CompletedChecks,
                                0,
                                2),
                            TotalSteps,
                            mappedStep,
                            value.Message));
                });

        WindowsRepairAssessmentResult verification;
        try
        {
            verification =
                await _assessmentService.AssessAsync(
                    new WindowsRepairAssessmentRequest(
                        CheckComponentStore: true,
                        VerifyProtectedSystemFiles: true),
                    stopAfterCurrentStep,
                    verificationProgress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            issues.Add(
                _sanitizer.Sanitize(ex.Message));
            AddSkippedRemaining(
                steps,
                2,
                "Verification could not start because the verification service failed safely.");
            return Complete(
                WindowsRepairExecutionOutcome.Failed,
                "The repair commands finished, but fresh read-only verification could not be completed.",
                verification: null,
                stopRequested: false);
        }

        foreach (var check in verification.Checks)
        {
            steps.Add(MapVerificationResult(check));
        }

        EnsureAllVerificationStepsPresent(
            steps,
            verification.StopRequested
                ? "Skipped because Stop after current step was requested."
                : "The verification check was not returned by the assessment service.");

        issues.AddRange(
            verification.Issues
                .Select(_sanitizer.Sanitize)
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item)));

        var outcome =
            verification.StopRequested
                ? WindowsRepairExecutionOutcome.Stopped
                : verification.OverallOutcome switch
                {
                    WindowsRepairAssessmentOutcome.Healthy =>
                        WindowsRepairExecutionOutcome.Completed,
                    WindowsRepairAssessmentOutcome.Attention or
                    WindowsRepairAssessmentOutcome.Inconclusive =>
                        WindowsRepairExecutionOutcome
                            .CompletedWithAttention,
                    _ =>
                        WindowsRepairExecutionOutcome.Failed
                };

        var summary = outcome switch
        {
            WindowsRepairExecutionOutcome.Completed =>
                "DISM RestoreHealth, SFC Scannow, and both fresh read-only verification checks completed. The verification reported no classified integrity issue.",
            WindowsRepairExecutionOutcome.CompletedWithAttention =>
                "The repair commands completed, but fresh verification still requires review. PC-SPA did not claim success beyond the Microsoft evidence.",
            WindowsRepairExecutionOutcome.Stopped =>
                "The current Microsoft step finished normally, then remaining guided-repair steps were skipped.",
            _ =>
                "The guided repair or its verification did not complete successfully."
        };

        return Complete(
            outcome,
            summary,
            verification,
            verification.StopRequested);

        WindowsRepairExecutionResult Complete(
            WindowsRepairExecutionOutcome outcome,
            string summary,
            WindowsRepairAssessmentResult? verification,
            bool stopRequested) =>
            new(
                referenceId,
                assessment.ReferenceId,
                startedUtc,
                _utcNow().ToUniversalTime(),
                assessment.ApplicationVersion,
                assessment.BuildIdentifier,
                outcome,
                _sanitizer.Sanitize(summary),
                steps.ToArray(),
                verification,
                stopRequested,
                AutomaticRestartAttempted: false,
                issues
                    .Where(item =>
                        !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray());
    }

    private WindowsRepairExecutionStepResult
        InterpretRepairCommand(
            WindowsRepairExecutionCommandRequest request,
            WindowsRepairExecutionCommandResult command)
    {
        var output = SanitizeCapturedText(
            command.StandardOutput);
        var error = SanitizeCapturedText(
            string.IsNullOrWhiteSpace(
                command.StartFailure)
                ? command.StandardError
                : command.StartFailure);

        var succeeded =
            command.Started &&
            command.ExitCode == 0;

        return new WindowsRepairExecutionStepResult(
            request.Step,
            succeeded
                ? WindowsRepairExecutionStepOutcome.Succeeded
                : WindowsRepairExecutionStepOutcome.Failed,
            GetStepTitle(request.Step),
            succeeded
                ? "The approved Microsoft repair command completed with exit code 0."
                : command.Started
                    ? $"The approved Microsoft repair command completed with exit code {command.ExitCode}."
                    : "The approved Microsoft repair command did not start.",
            ChangesWindows: true,
            Path.GetFileName(request.ExecutablePath) ??
                string.Empty,
            request.Arguments.ToArray(),
            command.ExitCode,
            command.StartedUtc,
            command.FinishedUtc,
            output,
            error);
    }

    private WindowsRepairExecutionStepResult
        MapVerificationResult(
            WindowsRepairCheckResult check)
    {
        var step = check.Check switch
        {
            WindowsRepairAssessmentCheck
                .ComponentStoreCheckHealth =>
                WindowsRepairExecutionStep
                    .ComponentStoreVerification,
            WindowsRepairAssessmentCheck
                .ProtectedSystemFilesVerifyOnly =>
                WindowsRepairExecutionStep
                    .ProtectedSystemFilesVerification,
            _ => throw new ArgumentOutOfRangeException(
                nameof(check),
                check.Check,
                "Unsupported verification check.")
        };

        var outcome = check.Outcome switch
        {
            WindowsRepairAssessmentOutcome.Healthy =>
                WindowsRepairExecutionStepOutcome.Succeeded,
            WindowsRepairAssessmentOutcome.Attention or
            WindowsRepairAssessmentOutcome.Inconclusive =>
                WindowsRepairExecutionStepOutcome.Attention,
            WindowsRepairAssessmentOutcome.Skipped =>
                WindowsRepairExecutionStepOutcome.Skipped,
            _ =>
                WindowsRepairExecutionStepOutcome.Failed
        };

        return new WindowsRepairExecutionStepResult(
            step,
            outcome,
            GetStepTitle(step),
            check.Summary,
            ChangesWindows: false,
            check.ExecutableName,
            check.Arguments,
            check.ExitCode,
            check.StartedUtc,
            check.FinishedUtc,
            check.SanitizedOutput,
            check.SanitizedError);
    }

    private void AddSkippedRemaining(
        ICollection<WindowsRepairExecutionStepResult> steps,
        int completedCount,
        string reason)
    {
        var orderedSteps = Enum
            .GetValues<WindowsRepairExecutionStep>();

        foreach (var step in orderedSteps.Skip(completedCount))
        {
            steps.Add(CreateSkipped(step, reason));
        }
    }

    private void EnsureAllVerificationStepsPresent(
        ICollection<WindowsRepairExecutionStepResult> steps,
        string reason)
    {
        foreach (var step in new[]
        {
            WindowsRepairExecutionStep
                .ComponentStoreVerification,
            WindowsRepairExecutionStep
                .ProtectedSystemFilesVerification
        })
        {
            if (steps.Any(item => item.Step == step))
            {
                continue;
            }

            steps.Add(CreateSkipped(step, reason));
        }
    }

    private WindowsRepairExecutionStepResult CreateSkipped(
        WindowsRepairExecutionStep step,
        string reason)
    {
        var now = _utcNow().ToUniversalTime();

        return new WindowsRepairExecutionStepResult(
            step,
            WindowsRepairExecutionStepOutcome.Skipped,
            GetStepTitle(step),
            _sanitizer.Sanitize(reason),
            ChangesWindows:
                step is
                    WindowsRepairExecutionStep
                        .ComponentStoreRepair or
                    WindowsRepairExecutionStep
                        .ProtectedSystemFilesRepair,
            string.Empty,
            Array.Empty<string>(),
            null,
            now,
            now,
            string.Empty,
            string.Empty);
    }

    private string SanitizeCapturedText(string? value) =>
        _sanitizer.Sanitize(
            string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\0", string.Empty));

    private static WindowsRepairExecutionStep?
        MapVerificationStep(
            WindowsRepairAssessmentCheck? check) =>
        check switch
        {
            WindowsRepairAssessmentCheck
                .ComponentStoreCheckHealth =>
                WindowsRepairExecutionStep
                    .ComponentStoreVerification,
            WindowsRepairAssessmentCheck
                .ProtectedSystemFilesVerifyOnly =>
                WindowsRepairExecutionStep
                    .ProtectedSystemFilesVerification,
            _ => null
        };

    public static string GetStepTitle(
        WindowsRepairExecutionStep step) =>
        step switch
        {
            WindowsRepairExecutionStep.ComponentStoreRepair =>
                "DISM RestoreHealth",
            WindowsRepairExecutionStep
                .ProtectedSystemFilesRepair =>
                "SFC Scannow",
            WindowsRepairExecutionStep
                .ComponentStoreVerification =>
                "DISM CheckHealth verification",
            WindowsRepairExecutionStep
                .ProtectedSystemFilesVerification =>
                "SFC VerifyOnly verification",
            _ => "Microsoft Windows repair step"
        };

    private string CreateReferenceId(
        DateTimeOffset timestamp)
    {
        var suffix = _referenceFactory()
            .ToString("N")[..8]
            .ToUpperInvariant();

        return
            $"REPAIR-{timestamp:yyyyMMddHHmmss}-{suffix}";
    }

    private static long? ReadWindowsDriveFreeSpace()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var windowsDirectory =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Windows);
            var root =
                Path.GetPathRoot(windowsDirectory);

            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            return drive.IsReady
                ? drive.AvailableFreeSpace
                : null;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double gigabyte =
            1024d * 1024d * 1024d;

        return $"{bytes / gigabyte:0.0} GB";
    }
}

using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed class WindowsRepairAssessmentService :
    IWindowsRepairAssessmentService
{
    private const long MinimumEvidenceFreeBytes =
        50L * 1024 * 1024;

    private readonly IWindowsRepairCommandRunner _commandRunner;
    private readonly DiagnosticPathSanitizer _sanitizer;
    private readonly Func<WindowsRepairEnvironmentStatus>
        _environmentProvider;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<(string Version, string BuildIdentifier)>
        _versionProvider;

    public WindowsRepairAssessmentService(
        IWindowsRepairCommandRunner commandRunner,
        DiagnosticPathSanitizer? sanitizer = null,
        Func<WindowsRepairEnvironmentStatus>? environmentProvider = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<(string Version, string BuildIdentifier)>?
            versionProvider = null)
    {
        _commandRunner = commandRunner ??
            throw new ArgumentNullException(nameof(commandRunner));
        _sanitizer = sanitizer ?? new DiagnosticPathSanitizer();
        _environmentProvider = environmentProvider ??
            CaptureEnvironment;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _versionProvider = versionProvider ?? ReadVersion;
    }

    public async Task<WindowsRepairAssessmentResult> AssessAsync(
        WindowsRepairAssessmentRequest request,
        Func<bool>? stopAfterCurrentCheck = null,
        IProgress<WindowsRepairAssessmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc = _utcNow().ToUniversalTime();
        var referenceId = CreateReferenceId(startedUtc);
        var environment = _environmentProvider();
        var version = _versionProvider();
        var selectedChecks = request.GetSelectedChecks();
        var results = new List<WindowsRepairCheckResult>();
        var issues = new List<string>();

        if (!request.HasSelectedChecks)
        {
            issues.Add(
                "No read-only Windows assessment check was selected.");

            return Complete(
                WindowsRepairAssessmentOutcome.Unsupported,
                stopRequested: false);
        }

        var blockingIssues = GetBlockingIssues(
            environment,
            selectedChecks);
        issues.AddRange(blockingIssues);

        if (blockingIssues.Count > 0)
        {
            foreach (var check in selectedChecks)
            {
                results.Add(CreateSkipped(
                    check,
                    "The check was not started because the environment preflight did not pass.",
                    userStopRequested: false));
            }

            progress?.Report(new WindowsRepairAssessmentProgress(
                selectedChecks.Count,
                selectedChecks.Count,
                null,
                "Assessment preflight did not pass. No Microsoft command was started."));

            return Complete(
                WindowsRepairAssessmentOutcome.Unsupported,
                stopRequested: false);
        }

        var completedChecks = 0;
        for (var index = 0; index < selectedChecks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var check = selectedChecks[index];
            progress?.Report(new WindowsRepairAssessmentProgress(
                completedChecks,
                selectedChecks.Count,
                check,
                $"Running {GetTitle(check)}. This is a read-only Microsoft Windows check."));

            var commandRequest = CreateCommandRequest(
                check,
                environment.WindowsDirectory);

            var commandResult = await _commandRunner
                .RunAsync(commandRequest, cancellationToken)
                .ConfigureAwait(false);

            var interpreted = Interpret(
                check,
                commandRequest,
                commandResult,
                userStopRequested: false);
            results.Add(interpreted);
            completedChecks++;

            var stopRequested =
                stopAfterCurrentCheck?.Invoke() ?? false;

            var remainingCheckCount =
                selectedChecks.Count - completedChecks;

            progress?.Report(new WindowsRepairAssessmentProgress(
                completedChecks,
                selectedChecks.Count,
                check,
                stopRequested
                    ? remainingCheckCount > 0
                        ? "The current check finished. Remaining selected checks will be skipped."
                        : "The final selected check finished normally. No additional selected checks remained."
                    : $"{GetTitle(check)} completed."));

            if (!stopRequested)
            {
                continue;
            }

            for (var remainingIndex = index + 1;
                 remainingIndex < selectedChecks.Count;
                 remainingIndex++)
            {
                results.Add(CreateSkipped(
                    selectedChecks[remainingIndex],
                    "Skipped because the user requested Stop after current check.",
                    userStopRequested: true));
                completedChecks++;
            }

            return Complete(
                CalculateOverallOutcome(results),
                stopRequested: true);
        }

        return Complete(
            CalculateOverallOutcome(results),
            stopRequested: false);

        WindowsRepairAssessmentResult Complete(
            WindowsRepairAssessmentOutcome outcome,
            bool stopRequested) =>
            new(
                referenceId,
                startedUtc,
                _utcNow().ToUniversalTime(),
                version.Version,
                version.BuildIdentifier,
                environment,
                results.ToArray(),
                outcome,
                stopRequested,
                issues
                    .Select(_sanitizer.Sanitize)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray());
    }

    private WindowsRepairCheckResult Interpret(
        WindowsRepairAssessmentCheck check,
        WindowsRepairCommandRequest request,
        WindowsRepairCommandResult command,
        bool userStopRequested)
    {
        var output = _sanitizer.Sanitize(
            NormalizeCapturedText(command.StandardOutput));
        var error = _sanitizer.Sanitize(
            NormalizeCapturedText(
                string.IsNullOrWhiteSpace(command.StartFailure)
                    ? command.StandardError
                    : command.StartFailure));

        if (!command.Started)
        {
            return new WindowsRepairCheckResult(
                check,
                WindowsRepairAssessmentOutcome.Failed,
                GetTitle(check),
                "The Microsoft assessment command did not start.",
                Path.GetFileName(request.ExecutablePath) ??
                    string.Empty,
                request.Arguments.ToArray(),
                null,
                command.StartedUtc,
                command.FinishedUtc,
                output,
                error,
                userStopRequested,
                "No repair or system change was attempted.");
        }

        if (command.ExitCode != 0)
        {
            return new WindowsRepairCheckResult(
                check,
                WindowsRepairAssessmentOutcome.Failed,
                GetTitle(check),
                $"The Microsoft assessment command completed with exit code {command.ExitCode}.",
                Path.GetFileName(request.ExecutablePath) ??
                    string.Empty,
                request.Arguments.ToArray(),
                command.ExitCode,
                command.StartedUtc,
                command.FinishedUtc,
                output,
                error,
                userStopRequested,
                "A non-zero exit code does not prove corruption. Review the sanitized output and run a fresh assessment if needed.");
        }

        var combined = string.Concat(output, "\n", error);
        var outcome = InterpretText(check, combined);
        var summary = outcome switch
        {
            WindowsRepairAssessmentOutcome.Healthy =>
                "The Microsoft assessment reported no integrity problem for this check.",
            WindowsRepairAssessmentOutcome.Attention =>
                "The Microsoft assessment reported a condition that may need guided repair.",
            _ =>
                "The Microsoft command completed, but PC-SPA could not classify the result confidently."
        };

        return new WindowsRepairCheckResult(
            check,
            outcome,
            GetTitle(check),
            summary,
            Path.GetFileName(request.ExecutablePath) ??
                    string.Empty,
            request.Arguments.ToArray(),
            command.ExitCode,
            command.StartedUtc,
            command.FinishedUtc,
            output,
            error,
            userStopRequested,
            outcome == WindowsRepairAssessmentOutcome.Inconclusive
                ? "Output wording may vary by Windows language or version. PC-SPA does not guess."
                : "This check is read-only and does not prove a performance improvement.");
    }

    public static WindowsRepairAssessmentOutcome InterpretText(
        WindowsRepairAssessmentCheck check,
        string? output)
    {
        var text = NormalizeCapturedText(output)
            .ToLowerInvariant();

        return check switch
        {
            WindowsRepairAssessmentCheck.ComponentStoreCheckHealth
                when text.Contains(
                    "no component store corruption detected",
                    StringComparison.Ordinal) =>
                WindowsRepairAssessmentOutcome.Healthy,

            WindowsRepairAssessmentCheck.ComponentStoreCheckHealth
                when text.Contains(
                    "component store is repairable",
                    StringComparison.Ordinal) ||
                     text.Contains(
                         "component store corruption detected",
                         StringComparison.Ordinal) ||
                     text.Contains(
                         "component store cannot be repaired",
                         StringComparison.Ordinal) =>
                WindowsRepairAssessmentOutcome.Attention,

            WindowsRepairAssessmentCheck.ProtectedSystemFilesVerifyOnly
                when text.Contains(
                    "did not find any integrity violations",
                    StringComparison.Ordinal) =>
                WindowsRepairAssessmentOutcome.Healthy,

            WindowsRepairAssessmentCheck.ProtectedSystemFilesVerifyOnly
                when text.Contains(
                    "found integrity violations",
                    StringComparison.Ordinal) ||
                     text.Contains(
                         "found corrupt files",
                         StringComparison.Ordinal) =>
                WindowsRepairAssessmentOutcome.Attention,

            _ => WindowsRepairAssessmentOutcome.Inconclusive
        };
    }

    private static string NormalizeCapturedText(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\0", string.Empty);

    private static WindowsRepairCommandRequest CreateCommandRequest(
        WindowsRepairAssessmentCheck check,
        string windowsDirectory) =>
        check switch
        {
            WindowsRepairAssessmentCheck.ComponentStoreCheckHealth =>
                WindowsRepairCommandRequest.CreateDismCheckHealth(
                    windowsDirectory),
            WindowsRepairAssessmentCheck.ProtectedSystemFilesVerifyOnly =>
                WindowsRepairCommandRequest.CreateSfcVerifyOnly(
                    windowsDirectory),
            _ => throw new ArgumentOutOfRangeException(
                nameof(check),
                check,
                "Unsupported Windows repair assessment check.")
        };

    private static IReadOnlyList<string> GetBlockingIssues(
        WindowsRepairEnvironmentStatus environment,
        IReadOnlyList<WindowsRepairAssessmentCheck> checks)
    {
        var issues = new List<string>();

        if (!environment.IsWindows)
        {
            issues.Add(
                "Windows Repair Assessment is supported only on Windows.");
        }

        if (!environment.IsElevated)
        {
            issues.Add(
                "Administrator elevation is required for Microsoft DISM and SFC assessment commands.");
        }

        if (string.IsNullOrWhiteSpace(
                environment.WindowsDirectory) ||
            !Path.IsPathRooted(environment.WindowsDirectory))
        {
            issues.Add(
                "The Windows directory could not be resolved safely.");
        }

        if (checks.Contains(
                WindowsRepairAssessmentCheck.ComponentStoreCheckHealth) &&
            !environment.DismAvailable)
        {
            issues.Add("DISM.exe is not available.");
        }

        if (checks.Contains(
                WindowsRepairAssessmentCheck.ProtectedSystemFilesVerifyOnly) &&
            !environment.SfcAvailable)
        {
            issues.Add("sfc.exe is not available.");
        }

        if (environment.SystemDriveFreeBytes is long freeBytes &&
            freeBytes < MinimumEvidenceFreeBytes)
        {
            issues.Add(
                "The Windows drive has less than 50 MB free for local assessment evidence.");
        }

        return issues;
    }

    private WindowsRepairCheckResult CreateSkipped(
        WindowsRepairAssessmentCheck check,
        string reason,
        bool userStopRequested)
    {
        var now = _utcNow().ToUniversalTime();

        return new WindowsRepairCheckResult(
            check,
            WindowsRepairAssessmentOutcome.Skipped,
            GetTitle(check),
            reason,
            string.Empty,
            Array.Empty<string>(),
            null,
            now,
            now,
            string.Empty,
            string.Empty,
            userStopRequested,
            "No Microsoft command was started.");
    }

    private static WindowsRepairAssessmentOutcome
        CalculateOverallOutcome(
            IReadOnlyCollection<WindowsRepairCheckResult> checks)
    {
        if (checks.Count == 0)
        {
            return WindowsRepairAssessmentOutcome.NotRun;
        }

        if (checks.Any(item =>
                item.Outcome ==
                WindowsRepairAssessmentOutcome.Failed))
        {
            return WindowsRepairAssessmentOutcome.Failed;
        }

        if (checks.Any(item =>
                item.Outcome ==
                WindowsRepairAssessmentOutcome.Attention))
        {
            return WindowsRepairAssessmentOutcome.Attention;
        }

        if (checks.Any(item =>
                item.Outcome ==
                WindowsRepairAssessmentOutcome.Inconclusive))
        {
            return WindowsRepairAssessmentOutcome.Inconclusive;
        }

        if (checks.All(item =>
                item.Outcome ==
                WindowsRepairAssessmentOutcome.Skipped))
        {
            return WindowsRepairAssessmentOutcome.Skipped;
        }

        return WindowsRepairAssessmentOutcome.Healthy;
    }

    private static string GetTitle(
        WindowsRepairAssessmentCheck check) =>
        check switch
        {
            WindowsRepairAssessmentCheck.ComponentStoreCheckHealth =>
                "Windows component store",
            WindowsRepairAssessmentCheck.ProtectedSystemFilesVerifyOnly =>
                "Protected Windows files",
            _ => "Windows assessment"
        };

    private static string CreateReferenceId(
        DateTimeOffset timestamp) =>
        $"ASSESS-{timestamp:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..30]
            .ToUpperInvariant();

    private static WindowsRepairEnvironmentStatus CaptureEnvironment()
    {
        var isWindows = OperatingSystem.IsWindows();
        var windowsDirectory = isWindows
            ? Environment.GetFolderPath(
                Environment.SpecialFolder.Windows)
            : string.Empty;
        var systemDriveRoot =
            Path.GetPathRoot(windowsDirectory) ?? string.Empty;
        var issues = new List<string>();
        long? freeBytes = null;

        if (!string.IsNullOrWhiteSpace(systemDriveRoot))
        {
            try
            {
                var drive = new DriveInfo(systemDriveRoot);
                if (drive.IsReady)
                {
                    freeBytes = drive.AvailableFreeSpace;
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                System.Security.SecurityException)
            {
                issues.Add(
                    "Windows drive free space could not be read.");
            }
        }

        var dismPath = string.IsNullOrWhiteSpace(windowsDirectory)
            ? string.Empty
            : Path.Combine(
                windowsDirectory,
                "System32",
                "DISM.exe");
        var sfcPath = string.IsNullOrWhiteSpace(windowsDirectory)
            ? string.Empty
            : Path.Combine(
                windowsDirectory,
                "System32",
                "sfc.exe");

        return new WindowsRepairEnvironmentStatus(
            isWindows,
            IsElevated(),
            RuntimeInformation.OSDescription,
            windowsDirectory,
            systemDriveRoot,
            File.Exists(dismPath),
            File.Exists(sfcPath),
            freeBytes,
            issues);
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(
                WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return false;
        }
    }

    private static (string Version, string BuildIdentifier)
        ReadVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ??
            typeof(WindowsRepairAssessmentService).Assembly;
        var version = assembly.GetName().Version;
        var displayVersion = version is null
            ? "1.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
        var buildIdentifier = assembly
            .GetCustomAttribute<
                AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? displayVersion;

        return (displayVersion, buildIdentifier);
    }
}

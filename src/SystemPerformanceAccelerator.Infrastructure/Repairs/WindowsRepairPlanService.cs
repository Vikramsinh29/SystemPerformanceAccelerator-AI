using System.Security.Principal;
using Microsoft.Win32;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed class WindowsRepairPlanService :
    IWindowsRepairPlanService
{
    public static readonly TimeSpan MaximumAssessmentAge =
        TimeSpan.FromHours(24);

    private readonly Func<WindowsRepairPlanRuntimeStatus>
        _runtimeProvider;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<Guid> _referenceFactory;

    public WindowsRepairPlanService(
        Func<WindowsRepairPlanRuntimeStatus>? runtimeProvider = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<Guid>? referenceFactory = null)
    {
        _runtimeProvider = runtimeProvider ??
            CaptureRuntimeStatus;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _referenceFactory = referenceFactory ?? Guid.NewGuid;
    }

    public WindowsRepairPlan CreatePlan(
        WindowsRepairAssessmentResult assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        var now = _utcNow().ToUniversalTime();
        var runtime = _runtimeProvider();
        var preflight = new List<
            WindowsRepairPlanPreflightItem>();

        AddAssessmentPreflight(
            assessment,
            now,
            preflight);
        AddRuntimePreflight(runtime, preflight);

        var assessmentHealthy =
            assessment.OverallOutcome ==
            WindowsRepairAssessmentOutcome.Healthy &&
            assessment.Checks.Count > 0 &&
            assessment.Checks.All(check =>
                check.Outcome ==
                WindowsRepairAssessmentOutcome.Healthy);

        var assessmentNeedsAttention =
            assessment.OverallOutcome ==
                WindowsRepairAssessmentOutcome.Attention ||
            assessment.Checks.Any(check =>
                check.Outcome ==
                WindowsRepairAssessmentOutcome.Attention);

        var hasBlocker = preflight.Any(item =>
            item.Status ==
            WindowsRepairPlanItemStatus.Blocked);

        var decision = assessmentHealthy
            ? WindowsRepairPlanDecision.NotRecommended
            : assessmentNeedsAttention && !hasBlocker
                ? WindowsRepairPlanDecision.ReviewRequired
                : WindowsRepairPlanDecision.Blocked;

        var isProposed =
            decision ==
            WindowsRepairPlanDecision.ReviewRequired;
        var steps = CreateSteps(isProposed);

        var (title, summary) = decision switch
        {
            WindowsRepairPlanDecision.NotRecommended =>
                (
                    "Repair is not recommended",
                    "The latest read-only assessment did not classify an integrity problem. PC-SPA will not propose a Windows repair without evidence."
                ),
            WindowsRepairPlanDecision.ReviewRequired =>
                (
                    "Eligible for future guided review",
                    "The latest assessment contains an Attention result and the current read-only preflight has no blocker. This preview still cannot run or authorize a repair."
                ),
            _ =>
                (
                    "Repair planning is blocked",
                    "At least one safety condition is unresolved. Run a fresh assessment or resolve the blocked preflight item before considering any future guided repair."
                )
        };

        return new WindowsRepairPlan(
            CreateReferenceId(now),
            assessment.ReferenceId,
            now,
            assessment.ApplicationVersion,
            assessment.BuildIdentifier,
            decision,
            title,
            summary,
            preflight.ToArray(),
            steps,
            RequiresFreshExecutionConsent: true,
            AuthorizesRepair: false,
            "This is a read-only preview. It starts no repair command and is not consent. Any future repair must repeat preflight checks and request explicit confirmation at execution time. PC-SPA will not restart Windows automatically.");
    }

    private static void AddAssessmentPreflight(
        WindowsRepairAssessmentResult assessment,
        DateTimeOffset now,
        ICollection<WindowsRepairPlanPreflightItem> items)
    {
        var age = now - assessment.FinishedUtc.ToUniversalTime();
        var isFutureDated =
            age < TimeSpan.FromMinutes(-5);
        var isFresh =
            !isFutureDated &&
            age <= MaximumAssessmentAge;

        items.Add(new WindowsRepairPlanPreflightItem(
            "Latest assessment age",
            isFresh
                ? WindowsRepairPlanItemStatus.Passed
                : WindowsRepairPlanItemStatus.Blocked,
            isFutureDated
                ? "The saved assessment timestamp is in the future. Run a fresh assessment."
                : isFresh
                    ? $"The assessment is {FormatAge(age)} old and satisfies PC-SPA's 24-hour planning policy."
                    : $"The assessment is {FormatAge(age)} old. PC-SPA requires a fresh assessment before repair planning."));

        var assessmentStatus =
            assessment.OverallOutcome switch
            {
                WindowsRepairAssessmentOutcome.Healthy =>
                    WindowsRepairPlanItemStatus.Passed,
                WindowsRepairAssessmentOutcome.Attention =>
                    WindowsRepairPlanItemStatus.Attention,
                _ => WindowsRepairPlanItemStatus.Blocked
            };

        items.Add(new WindowsRepairPlanPreflightItem(
            "Assessment evidence",
            assessmentStatus,
            assessment.OverallOutcome switch
            {
                WindowsRepairAssessmentOutcome.Healthy =>
                    "No selected check reported a classified integrity problem, so repair is not justified.",
                WindowsRepairAssessmentOutcome.Attention =>
                    "At least one selected check reported a condition that may justify a separately confirmed guided repair.",
                WindowsRepairAssessmentOutcome.Inconclusive =>
                    "At least one result is Inconclusive. PC-SPA does not plan repair from unknown wording.",
                WindowsRepairAssessmentOutcome.Failed =>
                    "A Microsoft assessment command failed. Run a successful fresh assessment first.",
                WindowsRepairAssessmentOutcome.Unsupported =>
                    "The assessment preflight was unsupported. Repair planning remains blocked.",
                WindowsRepairAssessmentOutcome.Skipped =>
                    "The assessment was skipped or stopped before complete evidence was available.",
                _ =>
                    "No completed assessment evidence is available."
            }));

        items.Add(new WindowsRepairPlanPreflightItem(
            "Assessment issues",
            assessment.Issues.Count == 0
                ? WindowsRepairPlanItemStatus.Passed
                : WindowsRepairPlanItemStatus.Blocked,
            assessment.Issues.Count == 0
                ? "The assessment record contains no unresolved environment issue."
                : $"The assessment record contains {assessment.Issues.Count:N0} unresolved issue(s)."));
    }

    private static void AddRuntimePreflight(
        WindowsRepairPlanRuntimeStatus runtime,
        ICollection<WindowsRepairPlanPreflightItem> items)
    {
        items.Add(new WindowsRepairPlanPreflightItem(
            "Windows platform",
            runtime.IsWindows
                ? WindowsRepairPlanItemStatus.Passed
                : WindowsRepairPlanItemStatus.Blocked,
            runtime.IsWindows
                ? "Supported Windows environment detected."
                : "Guided Windows repair planning is available only on Windows."));

        items.Add(new WindowsRepairPlanPreflightItem(
            "Administrator session",
            runtime.IsElevated
                ? WindowsRepairPlanItemStatus.Passed
                : WindowsRepairPlanItemStatus.Blocked,
            runtime.IsElevated
                ? "PC-SPA is running with administrator permission."
                : "Administrator permission is required before any future Windows repair."));

        items.Add(new WindowsRepairPlanPreflightItem(
            "Microsoft repair tools",
            runtime.DismAvailable && runtime.SfcAvailable
                ? WindowsRepairPlanItemStatus.Passed
                : WindowsRepairPlanItemStatus.Blocked,
            runtime.DismAvailable && runtime.SfcAvailable
                ? "The required Microsoft DISM and SFC executables are available."
                : "One or more required Microsoft repair executables are unavailable."));

        items.Add(new WindowsRepairPlanPreflightItem(
            "Pending restart state",
            runtime.PendingRestartDetected switch
            {
                false => WindowsRepairPlanItemStatus.Passed,
                true => WindowsRepairPlanItemStatus.Blocked,
                null => WindowsRepairPlanItemStatus.Blocked
            },
            runtime.PendingRestartDetected switch
            {
                false =>
                    "No supported pending-restart marker was detected.",
                true =>
                    "Windows reports a pending restart. Restart and reassess before planning repair.",
                null =>
                    "PC-SPA could not determine the pending-restart state safely."
            }));

        items.Add(new WindowsRepairPlanPreflightItem(
            "Windows drive free-space reading",
            runtime.SystemDriveFreeBytes.HasValue
                ? WindowsRepairPlanItemStatus.Information
                : WindowsRepairPlanItemStatus.Blocked,
            runtime.SystemDriveFreeBytes is long freeBytes
                ? $"Current available space was recorded as {FormatBytes(freeBytes)}. A future execution sprint must define and re-check its repair threshold."
                : "Available Windows-drive space could not be read. Repair planning remains blocked."));

        items.Add(new WindowsRepairPlanPreflightItem(
            "Runtime preflight issues",
            runtime.Issues.Count == 0
                ? WindowsRepairPlanItemStatus.Passed
                : WindowsRepairPlanItemStatus.Blocked,
            runtime.Issues.Count == 0
                ? "No additional runtime preflight issue was recorded."
                : $"PC-SPA recorded {runtime.Issues.Count:N0} runtime preflight issue(s)."));
    }

    private static IReadOnlyList<WindowsRepairPlanStep>
        CreateSteps(bool isProposed) =>
        [
            new WindowsRepairPlanStep(
                1,
                "Repeat preflight and request consent",
                "Re-check assessment freshness, administrator permission, pending restart state, tool availability, and execution-time safety before presenting a separate confirmation.",
                isProposed,
                ChangesWindows: false,
                MayUseWindowsUpdate: false,
                RequiresFreshConsent: true,
                AutomaticRestart: false),
            new WindowsRepairPlanStep(
                2,
                "Repair the Windows component store",
                "A future controlled action may repair the Windows component store. Microsoft servicing may use Windows Update unless a separately designed local source policy is selected.",
                isProposed,
                ChangesWindows: true,
                MayUseWindowsUpdate: true,
                RequiresFreshConsent: true,
                AutomaticRestart: false),
            new WindowsRepairPlanStep(
                3,
                "Repair protected Windows files",
                "A future controlled action may replace corrupted protected Windows files using Microsoft servicing sources.",
                isProposed,
                ChangesWindows: true,
                MayUseWindowsUpdate: false,
                RequiresFreshConsent: true,
                AutomaticRestart: false),
            new WindowsRepairPlanStep(
                4,
                "Run fresh read-only verification",
                "Repeat the existing read-only component-store and protected-file checks and record the outcome honestly.",
                isProposed,
                ChangesWindows: false,
                MayUseWindowsUpdate: false,
                RequiresFreshConsent: false,
                AutomaticRestart: false)
        ];

    private string CreateReferenceId(DateTimeOffset timestamp)
    {
        var suffix = _referenceFactory()
            .ToString("N")[..8]
            .ToUpperInvariant();
        return $"PLAN-{timestamp:yyyyMMddHHmmss}-{suffix}";
    }

    private static WindowsRepairPlanRuntimeStatus
        CaptureRuntimeStatus()
    {
        var isWindows = OperatingSystem.IsWindows();
        var issues = new List<string>();

        if (!isWindows)
        {
            return new WindowsRepairPlanRuntimeStatus(
                IsWindows: false,
                IsElevated: false,
                DismAvailable: false,
                SfcAvailable: false,
                PendingRestartDetected: null,
                SystemDriveFreeBytes: null,
                issues);
        }

        var windowsDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        var systemDriveRoot =
            Path.GetPathRoot(windowsDirectory);
        long? freeBytes = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(systemDriveRoot))
            {
                var drive = new DriveInfo(systemDriveRoot);
                if (drive.IsReady)
                {
                    freeBytes = drive.AvailableFreeSpace;
                }
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            System.Security.SecurityException)
        {
            issues.Add(
                "Windows-drive free space could not be read.");
        }

        var dismAvailable = File.Exists(Path.Combine(
            windowsDirectory,
            "System32",
            "DISM.exe"));
        var sfcAvailable = File.Exists(Path.Combine(
            windowsDirectory,
            "System32",
            "sfc.exe"));

        return new WindowsRepairPlanRuntimeStatus(
            IsWindows: true,
            IsElevated: IsElevated(),
            dismAvailable,
            sfcAvailable,
            DetectPendingRestart(issues),
            freeBytes,
            issues);
    }

    private static bool? DetectPendingRestart(
        ICollection<string> issues)
    {
        try
        {
            using var localMachine =
                RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    RegistryView.Registry64);

            using var componentServicing =
                localMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            using var windowsUpdate =
                localMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            using var sessionManager =
                localMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager");

            return componentServicing is not null ||
                   windowsUpdate is not null ||
                   sessionManager?.GetValue(
                       "PendingFileRenameOperations") is not null;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            issues.Add(
                "Pending-restart state could not be read.");
            return null;
        }
    }

    private static bool IsElevated()
    {
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

    private static string FormatAge(TimeSpan age)
    {
        var safeAge = age < TimeSpan.Zero
            ? TimeSpan.Zero
            : age;

        return safeAge.TotalHours < 1
            ? $"{Math.Max(0, (int)safeAge.TotalMinutes):N0} minute(s)"
            : $"{safeAge.TotalHours:0.0} hour(s)";
    }

    private static string FormatBytes(long bytes)
    {
        const double gigabyte = 1024d * 1024d * 1024d;
        return bytes >= gigabyte
            ? $"{bytes / gigabyte:0.0} GB"
            : $"{bytes / (1024d * 1024d):0.0} MB";
    }
}

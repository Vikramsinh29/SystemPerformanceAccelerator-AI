using System.IO;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class HealthCheckService : IHealthCheckService
{
    private static readonly TimeSpan CpuSampleDuration =
        TimeSpan.FromMilliseconds(250);

    private const double MinimumHealthyDiskFreePercent = 15;
    private const long MinimumHealthyDiskFreeBytes = 10L * 1024 * 1024 * 1024;
    private const double CpuAttentionThresholdPercent = 90;
    private const double MemoryAttentionThresholdPercent = 85;

    private readonly ISystemMonitorService _systemMonitorService;
    private readonly IStartupItemService _startupItemService;
    private readonly Func<SystemDriveSpace> _systemDriveSpaceProvider;

    public HealthCheckService(
        ISystemMonitorService systemMonitorService,
        IStartupItemService startupItemService,
        Func<SystemDriveSpace>? systemDriveSpaceProvider = null)
    {
        _systemMonitorService = systemMonitorService ??
            throw new ArgumentNullException(nameof(systemMonitorService));
        _startupItemService = startupItemService ??
            throw new ArgumentNullException(nameof(startupItemService));
        _systemDriveSpaceProvider = systemDriveSpaceProvider ??
            ReadSystemDriveSpace;
    }

    public async Task<HealthCheckResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<HealthCheckItem>(capacity: 4);
        var recommendations = new List<HealthRecommendation>(capacity: 4);
        var errors = new List<string>();

        AddSystemDriveCheck(
            items,
            recommendations,
            errors,
            cancellationToken);
        await AddProcessorAndMemoryChecksAsync(
            items,
            recommendations,
            errors,
            cancellationToken);
        await AddStartupCheckAsync(
            items,
            recommendations,
            errors,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return new HealthCheckResult(
            items,
            recommendations,
            errors,
            DateTimeOffset.Now);
    }

    private void AddSystemDriveCheck(
        ICollection<HealthCheckItem> items,
        ICollection<HealthRecommendation> recommendations,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var drive = _systemDriveSpaceProvider();
            cancellationToken.ThrowIfCancellationRequested();

            if (drive.TotalBytes <= 0 ||
                drive.AvailableBytes < 0 ||
                drive.AvailableBytes > drive.TotalBytes)
            {
                const string message =
                    "Windows returned invalid system-drive capacity information.";
                items.Add(CreateUnknownItem("System drive", message));
                recommendations.Add(CreateUnknownRecommendation(
                    "System drive",
                    "Recheck system-drive capacity",
                    "Run Health Check again and confirm the Windows system drive is available. Do not treat unavailable capacity data as a healthy result.",
                    "Reliable free-space information is required before deciding whether storage cleanup is necessary."));
                errors.Add(message);
                return;
            }

            var freePercent = drive.AvailableBytes * 100d / drive.TotalBytes;
            var status = freePercent >= MinimumHealthyDiskFreePercent &&
                         drive.AvailableBytes >= MinimumHealthyDiskFreeBytes
                ? HealthCheckStatus.Good
                : HealthCheckStatus.Attention;

            items.Add(new HealthCheckItem(
                "System drive",
                $"{FormatBytes(drive.AvailableBytes)} free",
                $"{freePercent:0.0}% free of {FormatBytes(drive.TotalBytes)} on {drive.RootPath}. " +
                "Attention is shown below 15% or 10 GB free.",
                status));

            recommendations.Add(status == HealthCheckStatus.Attention
                ? new HealthRecommendation(
                    "System drive",
                    "Free space on the Windows drive",
                    "Use Cleaner or Large File Finder to review unneeded files, then keep at least 15% and 10 GB free. Review every item before deleting it.",
                    "Low system-drive space can interfere with Windows updates, temporary files, paging, and normal application performance.",
                    HealthRecommendationPriority.High)
                : new HealthRecommendation(
                    "System drive",
                    "Maintain healthy free space",
                    "No immediate action is required. Keep at least 15% and 10 GB free and recheck after large installations or file transfers.",
                    "Maintaining free space helps Windows updates, temporary storage, and paging operate reliably.",
                    HealthRecommendationPriority.Low));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"System-drive space could not be read: {ex.Message}";
            items.Add(CreateUnknownItem("System drive", message));
            recommendations.Add(CreateUnknownRecommendation(
                "System drive",
                "Recheck system-drive access",
                "Run Health Check again after confirming the Windows drive is online and accessible. Do not start storage cleanup from this unknown result.",
                "A missing drive reading prevents the app from judging whether available space is healthy."));
            errors.Add(message);
        }
    }

    private async Task AddProcessorAndMemoryChecksAsync(
        ICollection<HealthCheckItem> items,
        ICollection<HealthRecommendation> recommendations,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _systemMonitorService.CaptureAsync(
                CpuSampleDuration,
                cancellationToken);

            var cpuStatus = snapshot.CpuUsagePercent >=
                CpuAttentionThresholdPercent
                ? HealthCheckStatus.Attention
                : HealthCheckStatus.Good;

            items.Add(new HealthCheckItem(
                "Current CPU usage",
                $"{snapshot.CpuUsagePercent:0.0}%",
                "A single read-only total-CPU sample. Attention is shown at 90% or higher.",
                cpuStatus));

            recommendations.Add(cpuStatus == HealthCheckStatus.Attention
                ? new HealthRecommendation(
                    "Current CPU usage",
                    "Confirm and reduce sustained CPU load",
                    "Close or pause nonessential high-CPU applications, wait one minute, and rerun Health Check. Use System Monitor to confirm whether usage remains high.",
                    "This result is one sample. Persistent high CPU use can reduce responsiveness, but a brief spike may be normal.",
                    HealthRecommendationPriority.Medium)
                : new HealthRecommendation(
                    "Current CPU usage",
                    "No current CPU action required",
                    "Keep the current workload. If the PC feels slow, use System Monitor and rerun Health Check while the slowdown is happening.",
                    "A normal sample indicates no immediate total-CPU pressure, but intermittent spikes may require observation during the actual problem.",
                    HealthRecommendationPriority.Low));

            if (snapshot.TotalPhysicalMemoryBytes <= 0)
            {
                const string message =
                    "Windows returned an invalid physical-memory total.";
                items.Add(CreateUnknownItem("Physical memory", message));
                recommendations.Add(CreateUnknownRecommendation(
                    "Physical memory",
                    "Recheck memory information",
                    "Run Health Check again and confirm Windows can report installed physical memory before making any upgrade or optimization decision.",
                    "An invalid memory total makes the usage percentage unreliable."));
                errors.Add(message);
                return;
            }

            var memoryStatus = snapshot.MemoryUsagePercent >=
                MemoryAttentionThresholdPercent
                ? HealthCheckStatus.Attention
                : HealthCheckStatus.Good;

            items.Add(new HealthCheckItem(
                "Physical memory",
                $"{snapshot.MemoryUsagePercent:0.0}% used",
                $"{FormatBytes(snapshot.UsedPhysicalMemoryBytes)} used of " +
                $"{FormatBytes(snapshot.TotalPhysicalMemoryBytes)}. " +
                "Attention is shown at 85% or higher.",
                memoryStatus));

            recommendations.Add(memoryStatus == HealthCheckStatus.Attention
                ? new HealthRecommendation(
                    "Physical memory",
                    "Reduce memory pressure",
                    "Close unused memory-heavy applications and browser tabs, then rerun Health Check during normal work. Consider more RAM only if high usage remains frequent.",
                    "Sustained high memory usage can increase paging and make applications respond slowly.",
                    HealthRecommendationPriority.High)
                : new HealthRecommendation(
                    "Physical memory",
                    "No current memory action required",
                    "Keep the current workload. Recheck while running your usual applications if you experience pauses or heavy paging.",
                    "The current reading shows available physical memory and no immediate memory-pressure warning.",
                    HealthRecommendationPriority.Low));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"CPU and memory could not be read: {ex.Message}";
            items.Add(CreateUnknownItem("Current CPU usage", message));
            items.Add(CreateUnknownItem("Physical memory", message));
            recommendations.Add(CreateUnknownRecommendation(
                "Current CPU usage",
                "Recheck CPU monitoring",
                "Run Health Check again and open System Monitor to confirm Windows can provide processor information.",
                "Without a valid sample, the app cannot distinguish normal load from sustained CPU pressure."));
            recommendations.Add(CreateUnknownRecommendation(
                "Physical memory",
                "Recheck memory monitoring",
                "Run Health Check again and open System Monitor to confirm Windows can provide physical-memory information.",
                "Without a valid reading, the app cannot determine whether memory pressure is affecting responsiveness."));
            errors.Add(message);
        }
    }

    private async Task AddStartupCheckAsync(
        ICollection<HealthCheckItem> items,
        ICollection<HealthRecommendation> recommendations,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _startupItemService.ScanAsync(
                cancellationToken: cancellationToken);

            var missingTargets = result.Items.Count(item =>
                item.TargetState == StartupTargetState.Missing);
            var malformedCommands = result.Items.Count(item =>
                item.TargetState == StartupTargetState.Malformed);
            var unresolvedTargets = result.Items.Count(item =>
                item.TargetState == StartupTargetState.Unresolved);
            var confirmedTargetIssues = missingTargets + malformedCommands;

            var status = confirmedTargetIssues > 0
                ? HealthCheckStatus.Attention
                : result.Errors.Count > 0 ||
                  unresolvedTargets > 0 ||
                  result.UnknownCount > 0
                    ? HealthCheckStatus.Unknown
                    : HealthCheckStatus.Good;

            var warningCount = result.Errors.Count;
            items.Add(new HealthCheckItem(
                "Startup inventory",
                $"{result.Items.Count:N0} items • {result.EnabledCount:N0} enabled",
                $"{result.DisabledCount:N0} disabled • {result.UnknownCount:N0} unknown • " +
                $"{missingTargets:N0} missing • {malformedCommands:N0} malformed • " +
                $"{unresolvedTargets:N0} unresolved • {warningCount:N0} scan warning(s).",
                status));

            recommendations.Add(status switch
            {
                HealthCheckStatus.Attention => new HealthRecommendation(
                    "Startup inventory",
                    "Review invalid startup entries",
                    $"Open Startup Manager and review the {missingTargets:N0} missing and {malformedCommands:N0} malformed item(s). Keep the review read-only in this version.",
                    "Invalid startup entries can create avoidable startup errors or delays even when the app does not modify them automatically.",
                    HealthRecommendationPriority.Medium),
                HealthCheckStatus.Unknown => new HealthRecommendation(
                    "Startup inventory",
                    "Review unresolved startup information",
                    $"Open Startup Manager and inspect {unresolvedTargets:N0} unresolved, {result.UnknownCount:N0} unknown, and {warningCount:N0} warning item(s). Do not assume unknown entries are safe or unsafe.",
                    "Incomplete startup information prevents a confident recommendation about boot-time impact.",
                    HealthRecommendationPriority.Medium),
                _ => new HealthRecommendation(
                    "Startup inventory",
                    "Keep the startup list reviewed",
                    "No invalid startup target was confirmed. Periodically review Startup Manager and keep only applications you intentionally want at sign-in.",
                    "A lean, intentional startup list can reduce sign-in work without disabling required software.",
                    HealthRecommendationPriority.Low)
            });

            foreach (var error in result.Errors)
            {
                errors.Add($"Startup inventory: {error}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"Startup inventory could not be read: {ex.Message}";
            items.Add(CreateUnknownItem("Startup inventory", message));
            recommendations.Add(CreateUnknownRecommendation(
                "Startup inventory",
                "Recheck startup inventory",
                "Run Health Check again and open Startup Manager to confirm the registry and Startup folders can be read.",
                "Without a complete inventory, the app cannot identify invalid or unnecessary startup entries reliably."));
            errors.Add(message);
        }
    }

    private static HealthCheckItem CreateUnknownItem(
        string name,
        string details) =>
        new(name, "Unavailable", details, HealthCheckStatus.Unknown);

    private static HealthRecommendation CreateUnknownRecommendation(
        string area,
        string title,
        string recommendation,
        string whyItMatters) =>
        new(
            area,
            title,
            recommendation,
            whyItMatters,
            HealthRecommendationPriority.Medium);

    private static SystemDriveSpace ReadSystemDriveSpace()
    {
        var windowsFolder = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        var rootPath = Path.GetPathRoot(windowsFolder);

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException(
                "The Windows system drive could not be determined.");
        }

        var drive = new DriveInfo(rootPath);
        if (!drive.IsReady)
        {
            throw new IOException(
                $"The Windows system drive {rootPath} is not ready.");
        }

        return new SystemDriveSpace(
            rootPath,
            drive.TotalSize,
            drive.AvailableFreeSpace);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}

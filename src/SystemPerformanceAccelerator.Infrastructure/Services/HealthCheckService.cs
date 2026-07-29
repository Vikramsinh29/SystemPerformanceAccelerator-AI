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
        var errors = new List<string>();

        AddSystemDriveCheck(items, errors, cancellationToken);
        await AddProcessorAndMemoryChecksAsync(
            items,
            errors,
            cancellationToken);
        await AddStartupCheckAsync(items, errors, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return new HealthCheckResult(
            items,
            errors,
            DateTimeOffset.Now);
    }

    private void AddSystemDriveCheck(
        ICollection<HealthCheckItem> items,
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
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"System-drive space could not be read: {ex.Message}";
            items.Add(CreateUnknownItem("System drive", message));
            errors.Add(message);
        }
    }

    private async Task AddProcessorAndMemoryChecksAsync(
        ICollection<HealthCheckItem> items,
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

            if (snapshot.TotalPhysicalMemoryBytes <= 0)
            {
                const string message =
                    "Windows returned an invalid physical-memory total.";
                items.Add(CreateUnknownItem("Physical memory", message));
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
            errors.Add(message);
        }
    }

    private async Task AddStartupCheckAsync(
        ICollection<HealthCheckItem> items,
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
            errors.Add(message);
        }
    }

    private static HealthCheckItem CreateUnknownItem(
        string name,
        string details) =>
        new(name, "Unavailable", details, HealthCheckStatus.Unknown);

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

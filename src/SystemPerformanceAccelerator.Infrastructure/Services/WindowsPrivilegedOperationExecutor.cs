using System.ComponentModel;
using System.Diagnostics;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class WindowsPrivilegedOperationExecutor : IPrivilegedOperationExecutor
{
    internal const string RestoreHealthOperation = "windows-repair-restore-health";
    internal const string ScanProtectedFilesOperation = "windows-repair-scan-protected-files";

    private readonly string _helperPath;
    private readonly IPrivilegedHelperProcessLauncher _launcher;

    public WindowsPrivilegedOperationExecutor()
        : this(
            Path.Combine(AppContext.BaseDirectory, "PC-SPA.PrivilegedHelper.exe"),
            new WindowsPrivilegedHelperProcessLauncher())
    {
    }

    internal WindowsPrivilegedOperationExecutor(
        string helperPath,
        IPrivilegedHelperProcessLauncher launcher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(helperPath);
        _helperPath = Path.GetFullPath(helperPath);
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    public async Task<PrivilegedOperationResult> ExecuteAsync(
        PrivilegedOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var operationName = request.Kind switch
        {
            PrivilegedOperationKind.WindowsRepairRestoreHealth =>
                RestoreHealthOperation,
            PrivilegedOperationKind.WindowsRepairScanProtectedFiles =>
                ScanProtectedFilesOperation,
            PrivilegedOperationKind.StartupManagerAllUsersStateChange =>
                null,
            _ => null
        };

        if (operationName is null)
        {
            return PrivilegedOperationResult.Rejected(
                "privileged_operation_not_supported",
                "This privileged operation is not connected to the elevated helper yet.");
        }

        if (!File.Exists(_helperPath))
        {
            return PrivilegedOperationResult.Rejected(
                "privileged_helper_missing",
                "The PC-SPA privileged helper is unavailable.");
        }

        try
        {
            var exitCode = await _launcher
                .LaunchAndWaitAsync(_helperPath, operationName, cancellationToken)
                .ConfigureAwait(false);

            return exitCode switch
            {
                0 => new PrivilegedOperationResult(
                    true,
                    true,
                    "completed",
                    "The privileged operation completed successfully."),
                1 => new PrivilegedOperationResult(
                    true,
                    false,
                    "operation_failed",
                    "Windows reported that the privileged operation did not complete successfully."),
                64 => PrivilegedOperationResult.Rejected(
                    "helper_rejected_request",
                    "The privileged helper rejected the operation request."),
                65 => PrivilegedOperationResult.Rejected(
                    "windows_directory_unavailable",
                    "The privileged helper could not locate the Windows directory."),
                66 => PrivilegedOperationResult.Rejected(
                    "repair_command_not_started",
                    "The approved Windows repair command could not be started."),
                _ => new PrivilegedOperationResult(
                    true,
                    false,
                    "helper_failed",
                    $"The privileged helper exited with code {exitCode}.")
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return PrivilegedOperationResult.Rejected(
                "uac_cancelled",
                "Administrator permission was cancelled. No privileged operation was started.");
        }
        catch (Win32Exception ex)
        {
            return PrivilegedOperationResult.Rejected(
                "uac_launch_failed",
                $"Windows could not start the privileged helper: {ex.Message}");
        }
    }
}

internal interface IPrivilegedHelperProcessLauncher
{
    Task<int> LaunchAndWaitAsync(
        string helperPath,
        string operationName,
        CancellationToken cancellationToken);
}

internal sealed class WindowsPrivilegedHelperProcessLauncher :
    IPrivilegedHelperProcessLauncher
{
    public async Task<int> LaunchAndWaitAsync(
        string helperPath,
        string operationName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(helperPath) ?? AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add(operationName);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            throw new Win32Exception("Windows did not start the privileged helper.");
        }

        // Once the elevated helper starts, let the approved operation finish normally.
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        return process.ExitCode;
    }
}

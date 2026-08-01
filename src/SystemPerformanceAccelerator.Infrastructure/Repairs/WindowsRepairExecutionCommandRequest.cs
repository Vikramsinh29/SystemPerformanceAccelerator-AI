using System.Diagnostics;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed record WindowsRepairExecutionCommandRequest(
    WindowsRepairExecutionStep Step,
    string ExecutablePath,
    IReadOnlyList<string> Arguments)
{
    private static readonly string[] DismRestoreHealthArguments =
    [
        "/Online",
        "/English",
        "/Cleanup-Image",
        "/RestoreHealth",
        "/NoRestart"
    ];

    private static readonly string[] SfcScanNowArguments =
    [
        "/scannow"
    ];

    public static WindowsRepairExecutionCommandRequest
        CreateDismRestoreHealth(string windowsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            windowsDirectory);

        return new WindowsRepairExecutionCommandRequest(
            WindowsRepairExecutionStep.ComponentStoreRepair,
            Path.Combine(
                Path.GetFullPath(windowsDirectory),
                "System32",
                "DISM.exe"),
            DismRestoreHealthArguments);
    }

    public static WindowsRepairExecutionCommandRequest
        CreateSfcScanNow(string windowsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            windowsDirectory);

        return new WindowsRepairExecutionCommandRequest(
            WindowsRepairExecutionStep.ProtectedSystemFilesRepair,
            Path.Combine(
                Path.GetFullPath(windowsDirectory),
                "System32",
                "sfc.exe"),
            SfcScanNowArguments);
    }

    public ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory =
                Path.GetDirectoryName(ExecutablePath) ??
                AppContext.BaseDirectory
        };

        foreach (var argument in Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public bool IsApprovedGuidedRepairCommand =>
        Step switch
        {
            WindowsRepairExecutionStep.ComponentStoreRepair =>
                Arguments.SequenceEqual(
                    DismRestoreHealthArguments,
                    StringComparer.OrdinalIgnoreCase),
            WindowsRepairExecutionStep.ProtectedSystemFilesRepair =>
                Arguments.SequenceEqual(
                    SfcScanNowArguments,
                    StringComparer.OrdinalIgnoreCase),
            _ => false
        };
}

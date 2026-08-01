using System.Diagnostics;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Infrastructure.Repairs;

public sealed record WindowsRepairCommandRequest(
    WindowsRepairAssessmentCheck Check,
    string ExecutablePath,
    IReadOnlyList<string> Arguments)
{
    private static readonly string[] DismArguments =
    [
        "/Online",
        "/English",
        "/Cleanup-Image",
        "/CheckHealth"
    ];

    private static readonly string[] SfcArguments =
    [
        "/verifyonly"
    ];

    public static WindowsRepairCommandRequest CreateDismCheckHealth(
        string windowsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsDirectory);

        return new WindowsRepairCommandRequest(
            WindowsRepairAssessmentCheck.ComponentStoreCheckHealth,
            Path.Combine(
                Path.GetFullPath(windowsDirectory),
                "System32",
                "DISM.exe"),
            DismArguments);
    }

    public static WindowsRepairCommandRequest CreateSfcVerifyOnly(
        string windowsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsDirectory);

        return new WindowsRepairCommandRequest(
            WindowsRepairAssessmentCheck.ProtectedSystemFilesVerifyOnly,
            Path.Combine(
                Path.GetFullPath(windowsDirectory),
                "System32",
                "sfc.exe"),
            SfcArguments);
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
            WorkingDirectory = Path.GetDirectoryName(ExecutablePath) ??
                AppContext.BaseDirectory
        };

        foreach (var argument in Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public bool IsStrictlyReadOnly =>
        Check switch
        {
            WindowsRepairAssessmentCheck.ComponentStoreCheckHealth =>
                Arguments.SequenceEqual(
                    DismArguments,
                    StringComparer.OrdinalIgnoreCase),
            WindowsRepairAssessmentCheck.ProtectedSystemFilesVerifyOnly =>
                Arguments.SequenceEqual(
                    SfcArguments,
                    StringComparer.OrdinalIgnoreCase),
            _ => false
        };
}

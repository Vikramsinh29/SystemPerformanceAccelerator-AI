using System.Text.Json;
using SystemPerformanceAccelerator.Infrastructure.Repairs;

namespace SystemPerformanceAccelerator.PrivilegedHelper;

internal static class Program
{
    internal const string RestoreHealthOperation =
        "windows-repair-restore-health";

    internal const string ScanProtectedFilesOperation =
        "windows-repair-scan-protected-files";

    internal const string CheckHealthOperation =
        "windows-repair-assess-check-health";

    internal const string VerifyProtectedFilesOperation =
        "windows-repair-assess-verify-protected-files";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            return 64;
        }

        var windowsDirectory =
            Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);

        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            return 65;
        }

        if (args.Length == 2)
        {
            return await RunAssessmentAsync(
                    args[0],
                    args[1],
                    windowsDirectory)
                .ConfigureAwait(false);
        }

        var request = args[0] switch
        {
            RestoreHealthOperation =>
                WindowsRepairExecutionCommandRequest
                    .CreateDismRestoreHealth(
                        windowsDirectory),

            ScanProtectedFilesOperation =>
                WindowsRepairExecutionCommandRequest
                    .CreateSfcScanNow(
                        windowsDirectory),

            _ => null
        };

        if (
            request is null ||
            !request.IsApprovedGuidedRepairCommand)
        {
            return 64;
        }

        var runner =
            new WindowsRepairExecutionCommandRunner();

        var result =
            await runner.RunAsync(request)
                .ConfigureAwait(false);

        if (!result.Started)
        {
            return 66;
        }

        return result.ExitCode == 0 ? 0 : 1;
    }

    private static async Task<int> RunAssessmentAsync(
        string operation,
        string token,
        string windowsDirectory)
    {
        WindowsRepairCommandRequest? request =
            operation switch
            {
                CheckHealthOperation =>
                    WindowsRepairCommandRequest
                        .CreateDismCheckHealth(
                            windowsDirectory),

                VerifyProtectedFilesOperation =>
                    WindowsRepairCommandRequest
                        .CreateSfcVerifyOnly(
                            windowsDirectory),

                _ => null
            };

        if (
            request is null ||
            !request.IsStrictlyReadOnly)
        {
            return 64;
        }

        string resultPath;

        try
        {
            resultPath =
                PrivilegedWindowsRepairAssessmentExchange
                    .GetResultPath(token);
        }
        catch
        {
            return 64;
        }

        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(resultPath)!);

            var runner =
                new WindowsRepairCommandRunner();

            var result =
                await runner.RunAsync(request)
                    .ConfigureAwait(false);

            var json =
                JsonSerializer.Serialize(result);

            await File.WriteAllTextAsync(
                    resultPath,
                    json)
                .ConfigureAwait(false);

            return 0;
        }
        catch
        {
            return 67;
        }
    }
}
using SystemPerformanceAccelerator.Infrastructure.Repairs;

namespace SystemPerformanceAccelerator.PrivilegedHelper;

internal static class Program
{
    internal const string RestoreHealthOperation = "windows-repair-restore-health";
    internal const string ScanProtectedFilesOperation = "windows-repair-scan-protected-files";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            return 64;
        }

        var windowsDirectory =
            Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            return 65;
        }

        var request = args[0] switch
        {
            RestoreHealthOperation =>
                WindowsRepairExecutionCommandRequest.CreateDismRestoreHealth(
                    windowsDirectory),
            ScanProtectedFilesOperation =>
                WindowsRepairExecutionCommandRequest.CreateSfcScanNow(
                    windowsDirectory),
            _ => null
        };

        if (request is null || !request.IsApprovedGuidedRepairCommand)
        {
            return 64;
        }

        var runner = new WindowsRepairExecutionCommandRunner();
        var result = await runner.RunAsync(request).ConfigureAwait(false);

        if (!result.Started)
        {
            return 66;
        }

        return result.ExitCode == 0 ? 0 : 1;
    }
}

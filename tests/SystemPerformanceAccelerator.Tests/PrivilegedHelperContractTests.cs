using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class PrivilegedHelperContractTests
{
    [Fact]
    public void Helper_RequiresAdministratorButDesktopManifestIsNotChangedByThisSprint()
    {
        var helperManifest = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.PrivilegedHelper",
            "app.manifest");
        var desktopManifest = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.Desktop",
            "app.manifest");

        Assert.Contains("requestedExecutionLevel level=\"requireAdministrator\"", helperManifest);
        Assert.Contains("requestedExecutionLevel level=\"requireAdministrator\"", desktopManifest);
    }

    [Fact]
    public void Helper_ExposesOnlyTwoFixedWindowsRepairOperations()
    {
        var program = ReadRepositoryFile(
            "src",
            "SystemPerformanceAccelerator.PrivilegedHelper",
            "Program.cs");

        Assert.Contains("windows-repair-restore-health", program);
        Assert.Contains("windows-repair-scan-protected-files", program);
        Assert.Contains("args.Length != 1", program);
        Assert.Contains("CreateDismRestoreHealth", program);
        Assert.Contains("CreateSfcScanNow", program);
        Assert.Contains("IsApprovedGuidedRepairCommand", program);

        Assert.DoesNotContain("ProcessStartInfo", program);
        Assert.DoesNotContain("cmd.exe", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecutablePath = args", program);
        Assert.DoesNotContain("Arguments = args", program);
    }

    [Fact]
    public void Solution_IncludesPrivilegedHelperAsSeparateProject()
    {
        var solution = ReadRepositoryFile("SystemPerformanceAccelerator.slnx");

        Assert.Contains(
            "src/SystemPerformanceAccelerator.PrivilegedHelper/SystemPerformanceAccelerator.PrivilegedHelper.csproj",
            solution);
    }

    private static string ReadRepositoryFile(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{Path.Combine(relativePath)}'.");
    }
}

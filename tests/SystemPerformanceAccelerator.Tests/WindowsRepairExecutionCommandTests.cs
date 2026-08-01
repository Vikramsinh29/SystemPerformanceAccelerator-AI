using System.Diagnostics;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsRepairExecutionCommandTests
{
    [Fact]
    public void DismRestoreHealth_UsesOnlyApprovedArguments()
    {
        var request =
            WindowsRepairExecutionCommandRequest
                .CreateDismRestoreHealth(
                    @"C:\Windows");

        Assert.Equal(
            WindowsRepairExecutionStep
                .ComponentStoreRepair,
            request.Step);
        Assert.Equal(
            @"C:\Windows\System32\DISM.exe",
            request.ExecutablePath);
        Assert.Equal(
            new[]
            {
                "/Online",
                "/English",
                "/Cleanup-Image",
                "/RestoreHealth",
                "/NoRestart"
            },
            request.Arguments);
        Assert.True(
            request.IsApprovedGuidedRepairCommand);
    }

    [Fact]
    public void SfcScanNow_UsesOnlyApprovedArgument()
    {
        var request =
            WindowsRepairExecutionCommandRequest
                .CreateSfcScanNow(
                    @"C:\Windows");

        Assert.Equal(
            WindowsRepairExecutionStep
                .ProtectedSystemFilesRepair,
            request.Step);
        Assert.Equal(
            @"C:\Windows\System32\sfc.exe",
            request.ExecutablePath);
        Assert.Equal(
            new[] { "/scannow" },
            request.Arguments);
        Assert.True(
            request.IsApprovedGuidedRepairCommand);
    }

    [Fact]
    public void StartInfo_HidesWindowAndRedirectsOutput()
    {
        var request =
            WindowsRepairExecutionCommandRequest
                .CreateDismRestoreHealth(
                    @"C:\Windows");

        var startInfo = request.CreateStartInfo();

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(
            ProcessWindowStyle.Hidden,
            startInfo.WindowStyle);
        Assert.True(
            startInfo.RedirectStandardOutput);
        Assert.True(
            startInfo.RedirectStandardError);
    }
}

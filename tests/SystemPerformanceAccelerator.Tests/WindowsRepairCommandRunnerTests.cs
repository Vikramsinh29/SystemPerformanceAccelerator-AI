using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsRepairCommandRunnerTests
{
    [Fact]
    public void DismRequest_UsesOnlyApprovedCheckHealthArguments()
    {
        var request =
            WindowsRepairCommandRequest.CreateDismCheckHealth(
                @"C:\Windows");

        Assert.Equal(
            WindowsRepairAssessmentCheck.ComponentStoreCheckHealth,
            request.Check);
        Assert.Equal(
            @"C:\Windows\System32\DISM.exe",
            request.ExecutablePath);
        Assert.Equal(
            new[] { "/Online", "/English", "/Cleanup-Image", "/CheckHealth" },
            request.Arguments);
        Assert.True(request.IsStrictlyReadOnly);
        Assert.DoesNotContain(
            request.Arguments,
            argument => argument.Contains(
                "RestoreHealth",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SfcRequest_UsesOnlyVerifyOnly()
    {
        var request =
            WindowsRepairCommandRequest.CreateSfcVerifyOnly(
                @"C:\Windows");

        Assert.Equal(
            WindowsRepairAssessmentCheck
                .ProtectedSystemFilesVerifyOnly,
            request.Check);
        Assert.Equal(
            @"C:\Windows\System32\sfc.exe",
            request.ExecutablePath);
        Assert.Equal(new[] { "/verifyonly" }, request.Arguments);
        Assert.True(request.IsStrictlyReadOnly);
        Assert.DoesNotContain(
            request.Arguments,
            argument => argument.Contains(
                "scannow",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateStartInfo_DoesNotUseShell()
    {
        var request =
            WindowsRepairCommandRequest.CreateDismCheckHealth(
                @"C:\Windows");

        var startInfo = request.CreateStartInfo();

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(request.ExecutablePath, startInfo.FileName);
        Assert.Equal(
            request.Arguments.ToArray(),
            startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void CreateStartInfo_UsesDefenseInDepthConsoleSuppression()
    {
        var request =
            WindowsRepairCommandRequest.CreateSfcVerifyOnly(
                @"C:\Windows");

        var startInfo = request.CreateStartInfo();

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(
            System.Diagnostics.ProcessWindowStyle.Hidden,
            startInfo.WindowStyle);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public async Task RunAsync_BlocksUnapprovedArgumentsBeforeStarting()
    {
        var request = new WindowsRepairCommandRequest(
            WindowsRepairAssessmentCheck.ComponentStoreCheckHealth,
            @"C:\Windows\System32\DISM.exe",
            ["/Online", "/Cleanup-Image", "/RestoreHealth"]);
        var runner = new WindowsRepairCommandRunner(
            () => DateTimeOffset.Parse(
                "2026-08-01T00:00:00Z"));

        var result = await runner.RunAsync(request);

        Assert.False(result.Started);
        Assert.Null(result.ExitCode);
        Assert.Contains(
            "blocked",
            result.StartFailure,
            StringComparison.OrdinalIgnoreCase);
    }
}

using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class PrivilegedWindowsRepairExecutionCommandRunnerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-18T00:00:00Z");

    [Fact]
    public async Task RestoreHealth_MapsToTypedPrivilegedRequest()
    {
        var executor = new RecordingPrivilegedExecutor(
            new PrivilegedOperationResult(
                true,
                true,
                "completed",
                "Repair completed."));
        var runner = new PrivilegedWindowsRepairExecutionCommandRunner(
            executor,
            NextTime);

        var result = await runner.RunAsync(
            WindowsRepairExecutionCommandRequest
                .CreateDismRestoreHealth(@"C:\Windows"));

        Assert.Single(executor.Requests);
        Assert.Equal(
            PrivilegedOperationKind.WindowsRepairRestoreHealth,
            executor.Requests[0].Kind);
        Assert.True(result.Started);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ScanProtectedFiles_MapsToTypedPrivilegedRequest()
    {
        var executor = new RecordingPrivilegedExecutor(
            new PrivilegedOperationResult(
                true,
                true,
                "completed",
                "Repair completed."));
        var runner = new PrivilegedWindowsRepairExecutionCommandRunner(
            executor,
            NextTime);

        var result = await runner.RunAsync(
            WindowsRepairExecutionCommandRequest
                .CreateSfcScanNow(@"C:\Windows"));

        Assert.Single(executor.Requests);
        Assert.Equal(
            PrivilegedOperationKind.WindowsRepairScanProtectedFiles,
            executor.Requests[0].Kind);
        Assert.True(result.Started);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task UacCancellation_RemainsFailedBeforeStart()
    {
        var executor = new RecordingPrivilegedExecutor(
            PrivilegedOperationResult.Rejected(
                "uac_cancelled",
                "Administrator permission was cancelled."));
        var runner = new PrivilegedWindowsRepairExecutionCommandRunner(
            executor,
            NextTime);

        var result = await runner.RunAsync(
            WindowsRepairExecutionCommandRequest
                .CreateDismRestoreHealth(@"C:\Windows"));

        Assert.False(result.Started);
        Assert.Null(result.ExitCode);
        Assert.Contains(
            "cancelled",
            result.StartFailure,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsupportedCommand_IsBlockedBeforeExecutor()
    {
        var executor = new RecordingPrivilegedExecutor(
            new PrivilegedOperationResult(
                true,
                true,
                "completed",
                "Repair completed."));
        var runner = new PrivilegedWindowsRepairExecutionCommandRunner(
            executor,
            NextTime);
        var request = new WindowsRepairExecutionCommandRequest(
            WindowsRepairExecutionStep.ComponentStoreRepair,
            @"C:\Windows\System32\DISM.exe",
            new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" });

        var result = await runner.RunAsync(request);

        Assert.Empty(executor.Requests);
        Assert.False(result.Started);
        Assert.Contains(
            "not an approved",
            result.StartFailure,
            StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset NextTime() => Now;

    private sealed class RecordingPrivilegedExecutor(
        PrivilegedOperationResult result) : IPrivilegedOperationExecutor
    {
        public List<PrivilegedOperationRequest> Requests { get; } = [];

        public Task<PrivilegedOperationResult> ExecuteAsync(
            PrivilegedOperationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }
}

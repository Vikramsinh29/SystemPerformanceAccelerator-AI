using System.ComponentModel;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsPrivilegedOperationExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RoutesRestoreHealthToFixedHelperOperation()
    {
        using var helper = TemporaryHelperFile.Create();
        var launcher = new RecordingLauncher(0);
        var executor = new WindowsPrivilegedOperationExecutor(helper.Path, launcher);

        var result = await executor.ExecuteAsync(
            PrivilegedOperationRequest.CreateWindowsRepairRestoreHealth());

        Assert.True(result.Started);
        Assert.True(result.Succeeded);
        Assert.Equal("completed", result.Code);
        Assert.Equal(helper.Path, launcher.HelperPath);
        Assert.Equal("windows-repair-restore-health", launcher.OperationName);
    }

    [Fact]
    public async Task ExecuteAsync_RoutesProtectedFileScanToFixedHelperOperation()
    {
        using var helper = TemporaryHelperFile.Create();
        var launcher = new RecordingLauncher(0);
        var executor = new WindowsPrivilegedOperationExecutor(helper.Path, launcher);

        var result = await executor.ExecuteAsync(
            PrivilegedOperationRequest.CreateWindowsRepairScanProtectedFiles());

        Assert.True(result.Succeeded);
        Assert.Equal("windows-repair-scan-protected-files", launcher.OperationName);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsStartupMutationUntilDedicatedHandoffExists()
    {
        using var helper = TemporaryHelperFile.Create();
        var launcher = new RecordingLauncher(0);
        var executor = new WindowsPrivilegedOperationExecutor(helper.Path, launcher);
        var item = CreateSafeAllUsersStartupItem();
        var request = PrivilegedOperationRequest.CreateAllUsersStartupStateChange(
            item,
            StartupItemState.Disabled);

        var result = await executor.ExecuteAsync(request);

        Assert.False(result.Started);
        Assert.False(result.Succeeded);
        Assert.Equal("privileged_operation_not_supported", result.Code);
        Assert.Null(launcher.OperationName);
    }

    [Fact]
    public async Task ExecuteAsync_MapsUserCancelledUacWithoutStartingOperation()
    {
        using var helper = TemporaryHelperFile.Create();
        var launcher = new ThrowingLauncher(new Win32Exception(1223));
        var executor = new WindowsPrivilegedOperationExecutor(helper.Path, launcher);

        var result = await executor.ExecuteAsync(
            PrivilegedOperationRequest.CreateWindowsRepairRestoreHealth());

        Assert.False(result.Started);
        Assert.False(result.Succeeded);
        Assert.Equal("uac_cancelled", result.Code);
    }

    [Fact]
    public async Task ExecuteAsync_FailsClosedWhenHelperIsMissing()
    {
        var launcher = new RecordingLauncher(0);
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
        var executor = new WindowsPrivilegedOperationExecutor(missing, launcher);

        var result = await executor.ExecuteAsync(
            PrivilegedOperationRequest.CreateWindowsRepairRestoreHealth());

        Assert.False(result.Started);
        Assert.Equal("privileged_helper_missing", result.Code);
        Assert.Null(launcher.OperationName);
    }

    private static StartupItem CreateSafeAllUsersStartupItem() =>
        new(
            "Example",
            @"C:\Program Files\Example\example.exe",
            "Registry — All users (64-bit)",
            @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run",
            StartupItemState.Enabled,
            StartupTargetState.Available)
        {
            Kind = StartupItemKind.RegistryRun,
            SourceScope = StartupItemScope.AllUsers,
            SourceRegistryView = StartupRegistryView.Registry64,
            EntryIdentifier = "Example",
            ApprovalScope = StartupItemScope.AllUsers,
            ApprovalRegistryView = StartupRegistryView.Registry64,
            ApprovalCategory = "Run"
        };

    private sealed class RecordingLauncher(int exitCode) : IPrivilegedHelperProcessLauncher
    {
        public string? HelperPath { get; private set; }
        public string? OperationName { get; private set; }

        public Task<int> LaunchAndWaitAsync(
            string helperPath,
            string operationName,
            CancellationToken cancellationToken)
        {
            HelperPath = helperPath;
            OperationName = operationName;
            return Task.FromResult(exitCode);
        }
    }

    private sealed class ThrowingLauncher(Exception exception) : IPrivilegedHelperProcessLauncher
    {
        public Task<int> LaunchAndWaitAsync(
            string helperPath,
            string operationName,
            CancellationToken cancellationToken) =>
            Task.FromException<int>(exception);
    }

    private sealed class TemporaryHelperFile : IDisposable
    {
        private TemporaryHelperFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryHelperFile Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                Guid.NewGuid() + ".exe");
            File.WriteAllBytes(path, []);
            return new TemporaryHelperFile(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}

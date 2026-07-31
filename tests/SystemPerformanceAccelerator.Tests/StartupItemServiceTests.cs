using System.Buffers.Binary;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Services;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class StartupItemServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"spa-startup-tests-{Guid.NewGuid():N}");

    public StartupItemServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ScanAsync_EnumeratesCurrentAndAllUsersStartupFoldersReadOnly()
    {
        var currentUserFolder = Directory.CreateDirectory(Path.Combine(_root, "current")).FullName;
        var allUsersFolder = Directory.CreateDirectory(Path.Combine(_root, "all-users")).FullName;
        var currentItem = Path.Combine(currentUserFolder, "CurrentUserTool.cmd");
        var allUsersItem = Path.Combine(allUsersFolder, "AllUsersTool.cmd");
        await File.WriteAllTextAsync(currentItem, "@echo off");
        await File.WriteAllTextAsync(allUsersItem, "@echo off");
        var service = new StartupItemService(
            currentUserFolder,
            allUsersFolder,
            scanRegistry: false);

        var result = await service.ScanAsync();

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, item =>
            item.Name == "CurrentUserTool" &&
            item.Command == currentItem &&
            item.Source.Contains("Current user", StringComparison.Ordinal) &&
            item.Location == currentItem &&
            item.State == StartupItemState.Enabled &&
            item.TargetState == StartupTargetState.Available &&
            item.Kind == StartupItemKind.StartupFolder &&
            item.SourceScope == StartupItemScope.CurrentUser &&
            item.EntryIdentifier == "CurrentUserTool.cmd" &&
            item.CanDisable);
        Assert.Contains(result.Items, item =>
            item.Name == "AllUsersTool" &&
            item.Command == allUsersItem &&
            item.Source.Contains("All users", StringComparison.Ordinal) &&
            item.Location == allUsersItem &&
            item.State == StartupItemState.Enabled &&
            item.TargetState == StartupTargetState.Available &&
            item.Kind == StartupItemKind.StartupFolder &&
            item.SourceScope == StartupItemScope.AllUsers &&
            item.EntryIdentifier == "AllUsersTool.cmd" &&
            item.CanDisable);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.LocationsScanned);
    }

    [Fact]
    public async Task ScanAsync_ReportsMissingFolderAndContinues()
    {
        var currentUserFolder = Directory.CreateDirectory(Path.Combine(_root, "current")).FullName;
        var existingItem = Path.Combine(currentUserFolder, "ExistingTool.cmd");
        await File.WriteAllTextAsync(existingItem, "@echo off");
        var missingAllUsersFolder = Path.Combine(_root, "missing");
        var service = new StartupItemService(
            currentUserFolder,
            missingAllUsersFolder,
            scanRegistry: false);

        var result = await service.ScanAsync();

        Assert.Single(result.Items);
        var error = Assert.Single(result.Errors);
        Assert.Contains("does not exist", error.ToLowerInvariant());
    }

    [Fact]
    public async Task ScanAsync_HandlesMalformedShortcutWithoutCrashing()
    {
        var currentUserFolder = Directory.CreateDirectory(Path.Combine(_root, "current")).FullName;
        var allUsersFolder = Directory.CreateDirectory(Path.Combine(_root, "all-users")).FullName;
        var malformedShortcut = Path.Combine(currentUserFolder, "Broken.lnk");
        await File.WriteAllTextAsync(malformedShortcut, "not a Windows shortcut");
        var service = new StartupItemService(
            currentUserFolder,
            allUsersFolder,
            scanRegistry: false);

        var result = await service.ScanAsync();

        var item = Assert.Single(result.Items);
        Assert.Equal("Broken", item.Name);
        Assert.Equal(StartupTargetState.Unresolved, item.TargetState);
        Assert.Single(result.Errors);
        Assert.True(File.Exists(malformedShortcut));
    }

    [Fact]
    public async Task ScanAsync_DeduplicatesSamePhysicalStartupFolderAndMarksIdentityAmbiguous()
    {
        var sharedFolder = Directory.CreateDirectory(Path.Combine(_root, "shared")).FullName;
        var startupItem = Path.Combine(sharedFolder, "SharedTool.cmd");
        await File.WriteAllTextAsync(startupItem, "@echo off");
        var service = new StartupItemService(
            sharedFolder,
            sharedFolder,
            scanRegistry: false);

        var result = await service.ScanAsync();

        var item = Assert.Single(result.Items);
        Assert.Contains("Current user", item.Source);
        Assert.Contains("All users", item.Source);
        Assert.True(item.HasAmbiguousStateIdentity);
        Assert.False(item.CanDisable);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ScanAsync_HonorsCancellation()
    {
        var currentUserFolder = Directory.CreateDirectory(Path.Combine(_root, "current")).FullName;
        var allUsersFolder = Directory.CreateDirectory(Path.Combine(_root, "all-users")).FullName;
        var service = new StartupItemService(
            currentUserFolder,
            allUsersFolder,
            scanRegistry: false);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ScanAsync(cancellationToken: cancellationTokenSource.Token));
    }

    [Fact]
    public async Task SetStateAsync_DisablesRevalidatedEntryWithoutDeletingSource()
    {
        var executable = await CreateTargetAsync("disable-target.exe");
        var item = CreateRegistryItem(
            "Disable Tool",
            executable,
            StartupItemState.Enabled,
            StartupTargetState.Available);
        var backend = new FakeStartupItemStateBackend(
            SnapshotFor(item));
        var service = CreateService(backend);

        var result = await service.SetStateAsync(
            item,
            StartupItemState.Disabled);

        Assert.Equal(StartupItemStateChangeOutcome.Changed, result.Outcome);
        Assert.True(result.Succeeded);
        Assert.Equal(1, backend.WriteCount);
        Assert.Equal(StartupItemState.Disabled, backend.Current.State);
        Assert.True(File.Exists(executable));
    }

    [Fact]
    public async Task SetStateAsync_EnablesRevalidatedAvailableEntry()
    {
        var executable = await CreateTargetAsync("enable-target.exe");
        var item = CreateRegistryItem(
            "Enable Tool",
            executable,
            StartupItemState.Disabled,
            StartupTargetState.Available);
        var backend = new FakeStartupItemStateBackend(
            SnapshotFor(item));
        var service = CreateService(backend);

        var result = await service.SetStateAsync(
            item,
            StartupItemState.Enabled);

        Assert.Equal(StartupItemStateChangeOutcome.Changed, result.Outcome);
        Assert.Equal(StartupItemState.Enabled, backend.Current.State);
        Assert.Equal(1, backend.WriteCount);
    }

    [Fact]
    public async Task SetStateAsync_RejectsCommandChangedAfterScan()
    {
        var executable = await CreateTargetAsync("original.exe");
        var replacement = await CreateTargetAsync("replacement.exe");
        var item = CreateRegistryItem(
            "Changed Tool",
            executable,
            StartupItemState.Enabled,
            StartupTargetState.Available);
        var backend = new FakeStartupItemStateBackend(
            SnapshotFor(item) with { Command = replacement });
        var service = CreateService(backend);

        var result = await service.SetStateAsync(
            item,
            StartupItemState.Disabled);

        Assert.Equal(StartupItemStateChangeOutcome.Stale, result.Outcome);
        Assert.Equal(0, backend.WriteCount);
    }

    [Fact]
    public async Task SetStateAsync_RejectsStartupFolderFileChangedAfterScan()
    {
        var itemPath = await CreateTargetAsync("folder-tool.cmd");
        var lastWrite = DateTimeOffset.UtcNow.AddMinutes(-5);
        var item = new StartupItem(
            "Folder Tool",
            itemPath,
            "Startup folder — Current user",
            itemPath,
            StartupItemState.Enabled,
            StartupTargetState.Available)
        {
            Kind = StartupItemKind.StartupFolder,
            SourceScope = StartupItemScope.CurrentUser,
            EntryIdentifier = "folder-tool.cmd",
            ApprovalScope = StartupItemScope.CurrentUser,
            ApprovalRegistryView = StartupRegistryView.Registry64,
            ApprovalCategory = "StartupFolder",
            SourceLengthBytes = 10,
            SourceLastWriteUtc = lastWrite
        };
        var backend = new FakeStartupItemStateBackend(
            new StartupItemStateSnapshot(
                true,
                item.Command,
                item.State,
                11,
                lastWrite));
        var service = CreateService(backend);

        var result = await service.SetStateAsync(
            item,
            StartupItemState.Disabled);

        Assert.Equal(StartupItemStateChangeOutcome.Stale, result.Outcome);
        Assert.Equal(0, backend.WriteCount);
    }

    [Fact]
    public async Task SetStateAsync_RejectsEnablingUnavailableTarget()
    {
        var missing = Path.Combine(_root, "missing-target.exe");
        var item = CreateRegistryItem(
            "Missing Tool",
            missing,
            StartupItemState.Disabled,
            StartupTargetState.Missing);
        var backend = new FakeStartupItemStateBackend(
            SnapshotFor(item));
        var service = CreateService(backend);

        var result = await service.SetStateAsync(
            item,
            StartupItemState.Enabled);

        Assert.Equal(StartupItemStateChangeOutcome.Unsupported, result.Outcome);
        Assert.Equal(0, backend.WriteCount);
    }

    [Fact]
    public async Task SetStateAsync_ReportsAccessDeniedWithoutDeletingEntry()
    {
        var executable = await CreateTargetAsync("protected.exe");
        var item = CreateRegistryItem(
            "Protected Tool",
            executable,
            StartupItemState.Enabled,
            StartupTargetState.Available);
        var backend = new FakeStartupItemStateBackend(
            SnapshotFor(item))
        {
            WriteException = new UnauthorizedAccessException("denied")
        };
        var service = CreateService(backend);

        var result = await service.SetStateAsync(
            item,
            StartupItemState.Disabled);

        Assert.Equal(StartupItemStateChangeOutcome.AccessDenied, result.Outcome);
        Assert.True(File.Exists(executable));
    }

    [Fact]
    public void ApprovalData_UsesWindowsEnabledAndDisabledMarkers()
    {
        var timestamp = new DateTimeOffset(
            2026,
            7,
            31,
            12,
            0,
            0,
            TimeSpan.Zero);

        var enabled = WindowsStartupItemStateBackend.CreateApprovalData(
            StartupItemState.Enabled,
            timestamp);
        var disabled = WindowsStartupItemStateBackend.CreateApprovalData(
            StartupItemState.Disabled,
            timestamp);

        Assert.Equal(12, enabled.Length);
        Assert.Equal((byte)0x02, enabled[0]);
        Assert.All(enabled.Skip(1), value => Assert.Equal((byte)0, value));
        Assert.Equal(12, disabled.Length);
        Assert.Equal((byte)0x03, disabled[0]);
        Assert.Equal(
            timestamp.UtcDateTime.ToFileTimeUtc(),
            BinaryPrimitives.ReadInt64LittleEndian(disabled.AsSpan(4, 8)));
    }

    [Fact]
    public async Task CommandInspector_RecognizesQuotedExistingTarget()
    {
        var executable = Path.Combine(_root, "tool with spaces.exe");
        await File.WriteAllBytesAsync(executable, [1, 2, 3]);

        var state = StartupCommandInspector.DetermineTargetState(
            $"\"{executable}\" --silent");

        Assert.Equal(StartupTargetState.Available, state);
    }

    [Fact]
    public void CommandInspector_ReportsMissingFullyQualifiedTarget()
    {
        var missingExecutable = Path.Combine(_root, "missing tool.exe");

        var state = StartupCommandInspector.DetermineTargetState(
            $"\"{missingExecutable}\" --silent");

        Assert.Equal(StartupTargetState.Missing, state);
    }

    [Fact]
    public void CommandInspector_ReportsMalformedQuotedCommand()
    {
        var state = StartupCommandInspector.DetermineTargetState(
            "\"C:\\Program Files\\Broken Startup Tool.exe --silent");

        Assert.Equal(StartupTargetState.Malformed, state);
    }

    private StartupItemService CreateService(
        IStartupItemStateBackend backend)
    {
        var currentFolder = Directory.CreateDirectory(
            Path.Combine(_root, "state-current")).FullName;
        var allUsersFolder = Directory.CreateDirectory(
            Path.Combine(_root, "state-all-users")).FullName;

        return new StartupItemService(
            currentFolder,
            allUsersFolder,
            scanRegistry: false,
            backend);
    }

    private static StartupItem CreateRegistryItem(
        string name,
        string command,
        StartupItemState state,
        StartupTargetState targetState) =>
        new(
            name,
            command,
            "Registry — Current user (64-bit)",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            state,
            targetState)
        {
            Kind = StartupItemKind.RegistryRun,
            SourceScope = StartupItemScope.CurrentUser,
            SourceRegistryView = StartupRegistryView.Registry64,
            EntryIdentifier = name,
            ApprovalScope = StartupItemScope.CurrentUser,
            ApprovalRegistryView = StartupRegistryView.Registry64,
            ApprovalCategory = "Run"
        };

    private static StartupItemStateSnapshot SnapshotFor(
        StartupItem item) =>
        new(
            true,
            item.Command,
            item.State,
            item.SourceLengthBytes,
            item.SourceLastWriteUtc);

    private async Task<string> CreateTargetAsync(string name)
    {
        var path = Path.Combine(_root, name);
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(
                _root,
                "*",
                SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeStartupItemStateBackend(
        StartupItemStateSnapshot snapshot) : IStartupItemStateBackend
    {
        public StartupItemStateSnapshot Current { get; private set; } = snapshot;

        public Exception? WriteException { get; init; }

        public int WriteCount { get; private set; }

        public StartupItemStateSnapshot Read(StartupItem item) => Current;

        public void Write(
            StartupItem item,
            StartupItemState requestedState)
        {
            WriteCount++;
            if (WriteException is not null)
            {
                throw WriteException;
            }

            Current = Current with { State = requestedState };
        }
    }
}

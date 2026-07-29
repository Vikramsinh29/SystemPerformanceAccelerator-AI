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
            item.TargetState == StartupTargetState.Available);
        Assert.Contains(result.Items, item =>
            item.Name == "AllUsersTool" &&
            item.Command == allUsersItem &&
            item.Source.Contains("All users", StringComparison.Ordinal) &&
            item.Location == allUsersItem &&
            item.State == StartupItemState.Enabled &&
            item.TargetState == StartupTargetState.Available);
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
    public async Task ScanAsync_DeduplicatesSamePhysicalStartupFolder()
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
    }
}

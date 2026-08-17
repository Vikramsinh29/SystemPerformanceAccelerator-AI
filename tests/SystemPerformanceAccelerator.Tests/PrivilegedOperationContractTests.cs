using System.Reflection;
using SystemPerformanceAccelerator.Core.Models;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class PrivilegedOperationContractTests
{
    [Fact]
    public void RepairRequests_ExposeOnlyApprovedIntentWithoutExecutableOrArguments()
    {
        var restore = PrivilegedOperationRequest.CreateWindowsRepairRestoreHealth();
        var scan = PrivilegedOperationRequest.CreateWindowsRepairScanProtectedFiles();

        Assert.Equal(PrivilegedOperationKind.WindowsRepairRestoreHealth, restore.Kind);
        Assert.Equal(PrivilegedOperationKind.WindowsRepairScanProtectedFiles, scan.Kind);
        Assert.Null(restore.StartupItem);
        Assert.Null(restore.RequestedStartupState);
        Assert.Null(scan.StartupItem);
        Assert.Null(scan.RequestedStartupState);

        var publicProperties = typeof(PrivilegedOperationRequest)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("ExecutablePath", publicProperties);
        Assert.DoesNotContain("Arguments", publicProperties);
        Assert.DoesNotContain("Command", publicProperties);
        Assert.Empty(typeof(PrivilegedOperationRequest).GetConstructors());
    }

    [Fact]
    public void AllUsersStartupStateChange_AcceptsSafeValidatedSnapshot()
    {
        var item = CreateSafeAllUsersStartupItem(StartupItemState.Enabled);

        var request = PrivilegedOperationRequest.CreateAllUsersStartupStateChange(
            item,
            StartupItemState.Disabled);

        Assert.Equal(
            PrivilegedOperationKind.StartupManagerAllUsersStateChange,
            request.Kind);
        Assert.Same(item, request.StartupItem);
        Assert.Equal(StartupItemState.Disabled, request.RequestedStartupState);
    }

    [Fact]
    public void AllUsersStartupStateChange_RejectsCurrentUserItem()
    {
        var item = CreateSafeAllUsersStartupItem(StartupItemState.Enabled) with
        {
            SourceScope = StartupItemScope.CurrentUser
        };

        Assert.Throws<ArgumentException>(() =>
            PrivilegedOperationRequest.CreateAllUsersStartupStateChange(
                item,
                StartupItemState.Disabled));
    }

    [Fact]
    public void AllUsersStartupStateChange_RejectsUnknownRequestedState()
    {
        var item = CreateSafeAllUsersStartupItem(StartupItemState.Enabled);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PrivilegedOperationRequest.CreateAllUsersStartupStateChange(
                item,
                StartupItemState.Unknown));
    }

    [Fact]
    public void AllUsersStartupStateChange_RejectsUnsafeOrIneligibleSnapshot()
    {
        var item = CreateSafeAllUsersStartupItem(StartupItemState.Enabled) with
        {
            HasAmbiguousStateIdentity = true
        };

        Assert.Throws<ArgumentException>(() =>
            PrivilegedOperationRequest.CreateAllUsersStartupStateChange(
                item,
                StartupItemState.Disabled));
    }

    private static StartupItem CreateSafeAllUsersStartupItem(
        StartupItemState state) =>
        new(
            "Example",
            @"C:\Program Files\Example\example.exe",
            "Registry — All users (64-bit)",
            @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run",
            state,
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
}

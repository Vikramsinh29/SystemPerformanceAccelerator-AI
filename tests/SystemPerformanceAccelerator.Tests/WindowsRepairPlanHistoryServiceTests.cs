using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class WindowsRepairPlanHistoryServiceTests
{
    [Fact]
    public async Task SaveAndLoadLatest_SanitizesPlanText()
    {
        using var folder = new TemporaryFolder();
        var service = new WindowsRepairPlanHistoryService(
            folder.Path,
            new DiagnosticPathSanitizer(
                userProfile: @"C:\Users\Alice",
                userName: "Alice"));
        var plan = CreatePlan(
            "PLAN-001",
            DateTimeOffset.Parse(
                "2026-08-01T10:00:00Z")) with
        {
            Summary =
                @"Review C:\Users\Alice\secret.txt before sharing."
        };

        await service.SaveAsync(plan);
        var loaded = service.LoadLatest();

        Assert.NotNull(loaded);
        Assert.DoesNotContain(
            @"C:\Users\Alice",
            loaded.Summary,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Alice",
            loaded.Summary,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            @"Review %USERPROFILE%\secret.txt before sharing.",
            loaded.Summary);
        Assert.False(loaded.AuthorizesRepair);
    }

    [Fact]
    public async Task SaveAsync_AppliesRecordCountRetention()
    {
        using var folder = new TemporaryFolder();
        var now = DateTimeOffset.Parse(
            "2026-08-01T10:00:00Z");
        var service = new WindowsRepairPlanHistoryService(
            folder.Path,
            utcNow: () => now,
            maximumRecordCount: 2);

        await service.SaveAsync(
            CreatePlan("PLAN-001", now.AddMinutes(-2)));
        await Task.Delay(20);
        await service.SaveAsync(
            CreatePlan("PLAN-002", now.AddMinutes(-1)));
        await Task.Delay(20);
        await service.SaveAsync(
            CreatePlan("PLAN-003", now));

        Assert.Equal(
            2,
            Directory.GetFiles(
                folder.Path,
                "PLAN-*.json").Length);
    }

    [Fact]
    public async Task DeleteHistory_RemovesSavedPlans()
    {
        using var folder = new TemporaryFolder();
        var service = new WindowsRepairPlanHistoryService(
            folder.Path);

        await service.SaveAsync(
            CreatePlan(
                "PLAN-001",
                DateTimeOffset.Parse(
                    "2026-08-01T10:00:00Z")));

        service.DeleteHistory();

        Assert.False(Directory.Exists(folder.Path));
        Assert.Null(service.LoadLatest());
    }

    private static WindowsRepairPlan CreatePlan(
        string referenceId,
        DateTimeOffset createdUtc) =>
        new(
            referenceId,
            "ASSESS-TEST",
            createdUtc,
            "1.0.0",
            "test-build",
            WindowsRepairPlanDecision.Blocked,
            "Repair planning is blocked",
            "Test plan.",
            new[]
            {
                new WindowsRepairPlanPreflightItem(
                    "Test preflight",
                    WindowsRepairPlanItemStatus.Blocked,
                    "Test detail.")
            },
            new[]
            {
                new WindowsRepairPlanStep(
                    1,
                    "Test step",
                    "Test purpose.",
                    IsProposed: false,
                    ChangesWindows: false,
                    MayUseWindowsUpdate: false,
                    RequiresFreshConsent: true,
                    AutomaticRestart: false)
            },
            RequiresFreshExecutionConsent: true,
            AuthorizesRepair: false,
            "No repair is authorized.");

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PC-SPA-PlanTests-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(
                        Path,
                        recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}

using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class DisabledWindowsRepairPlanHistoryService :
    IWindowsRepairPlanHistoryService
{
    public string PlanRoot => string.Empty;

    public Task SaveAsync(
        WindowsRepairPlan plan,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public WindowsRepairPlan? LoadLatest() => null;

    public void DeleteHistory()
    {
    }
}

using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IWindowsRepairPlanHistoryService
{
    string PlanRoot { get; }

    Task SaveAsync(
        WindowsRepairPlan plan,
        CancellationToken cancellationToken = default);

    WindowsRepairPlan? LoadLatest();

    void DeleteHistory();
}

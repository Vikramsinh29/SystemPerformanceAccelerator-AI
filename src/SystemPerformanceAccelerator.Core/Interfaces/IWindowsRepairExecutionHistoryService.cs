using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IWindowsRepairExecutionHistoryService
{
    string ExecutionRoot { get; }

    Task SaveAsync(
        WindowsRepairExecutionResult result,
        CancellationToken cancellationToken = default);

    WindowsRepairExecutionResult? LoadLatest();

    void DeleteHistory();
}

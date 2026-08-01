using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IWindowsRepairExecutionHistoryService
{
    string ExecutionRoot { get; }

    Task SaveAsync(
        WindowsRepairExecutionResult result,
        CancellationToken cancellationToken = default);

    WindowsRepairExecutionResult? LoadLatest();

    IReadOnlyList<WindowsRepairExecutionResult> LoadRecent(
        int maximumCount = 20);

    Task<string?> ExportLatestAsync(
        string destinationZipPath,
        CancellationToken cancellationToken = default);

    void DeleteHistory();
}

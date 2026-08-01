using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IWindowsRepairAssessmentHistoryService
{
    string AssessmentRoot { get; }

    Task SaveAsync(
        WindowsRepairAssessmentResult result,
        CancellationToken cancellationToken = default);

    WindowsRepairAssessmentResult? LoadLatest();

    Task<string?> ExportLatestAsync(
        string destinationZipPath,
        CancellationToken cancellationToken = default);

    void DeleteHistory();
}

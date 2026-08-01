using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public interface IWindowsRepairAssessmentInteractionService
{
    bool ConfirmAssessment(
        WindowsRepairAssessmentRequest request);

    bool ConfirmGuidedRepair(
        WindowsRepairExecutionReadiness readiness) =>
        false;

    string? ChooseReportDestination(
        string suggestedFileName);

    bool ConfirmDeleteHistory();

    void OpenFolder(string path);

    void ShowMessage(
        string title,
        string message,
        bool isError = false);
}

using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class NonInteractiveWindowsRepairAssessmentInteractionService :
    IWindowsRepairAssessmentInteractionService
{
    public bool ConfirmAssessment(
        WindowsRepairAssessmentRequest request) => false;

    public string? ChooseReportDestination(
        string suggestedFileName) => null;

    public bool ConfirmDeleteHistory() => false;

    public void OpenFolder(string path)
    {
    }

    public void ShowMessage(
        string title,
        string message,
        bool isError = false)
    {
    }
}

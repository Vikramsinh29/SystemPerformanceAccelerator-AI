using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class WindowsRepairAssessmentInteractionService :
    IWindowsRepairAssessmentInteractionService
{
    public bool ConfirmAssessment(
        WindowsRepairAssessmentRequest request)
    {
        var selectedChecks = request.GetSelectedChecks();
        var checkText = string.Join(
            "\n",
            selectedChecks.Select(check =>
                check switch
                {
                    WindowsRepairAssessmentCheck
                        .ComponentStoreCheckHealth =>
                        "• DISM component-store CheckHealth",
                    WindowsRepairAssessmentCheck
                        .ProtectedSystemFilesVerifyOnly =>
                        "• SFC protected-file VerifyOnly",
                    _ => "• Unsupported check"
                }));

        var message =
            "PC-SPA will run these Microsoft Windows checks:\n\n" +
            checkText +
            "\n\nThese checks are read-only. They do not repair files, clean the component store, run CHKDSK, schedule a restart, or claim a speed improvement." +
            "\n\nSFC may take several minutes. Stop after current check will not force-close a Microsoft process already running." +
            "\n\nContinue?";

        return MessageBox.Show(
            message,
            "Run read-only Windows assessment",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    public string? ChooseReportDestination(
        string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Windows repair assessment",
            Filter = "ZIP package (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = suggestedFileName,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    public bool ConfirmDeleteHistory() =>
        MessageBox.Show(
            "Delete all locally stored Windows repair assessment records?\n\nThis does not change Windows and cannot delete reports you exported elsewhere.",
            "Delete assessment history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { path },
            UseShellExecute = false
        });
    }

    public void ShowMessage(
        string title,
        string message,
        bool isError = false)
    {
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.OK,
            isError
                ? MessageBoxImage.Error
                : MessageBoxImage.Information);
    }
}

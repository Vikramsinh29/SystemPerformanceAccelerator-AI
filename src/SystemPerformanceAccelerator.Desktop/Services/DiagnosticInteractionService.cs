using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class DiagnosticInteractionService :
    IDiagnosticInteractionService
{
    public bool ConfirmExport(DiagnosticExportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var references = preview.ErrorReferences.Count == 0
            ? "No recorded error references."
            : string.Join(
                "\n",
                preview.ErrorReferences
                    .Take(10)
                    .Select(reference => $"• {reference}")) +
              (preview.ErrorReferences.Count > 10
                  ? $"\n• …and {preview.ErrorReferences.Count - 10:N0} more"
                  : string.Empty);

        var message =
            $"PC-SPA will create a local ZIP containing {preview.EventCount:N0} sanitized diagnostic event(s).\n\n" +
            $"Hardware summary: {(preview.IncludesHardwareSummary ? "Included" : "Not included")}\n\n" +
            $"Error references included:\n{references}\n\n" +
            $"{preview.PrivacyNotice}\n\n" +
            "After export, open and inspect the ZIP before sharing it. Continue?";

        return MessageBox.Show(
            message,
            "Preview diagnostic package",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    public string? SelectExportPath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export PC-SPA diagnostic package",
            FileName = suggestedFileName,
            DefaultExt = ".zip",
            AddExtension = true,
            Filter = "ZIP archive (*.zip)|*.zip",
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    public bool ConfirmFeedback(
        DiagnosticFeedbackRequest feedback,
        DiagnosticExportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(preview);

        var message =
            "Review the complete error report before sending it.\n\n" +
            $"Error reference: {feedback.ErrorReference}\n" +
            $"Affected area: {feedback.AffectedArea}\n" +
            $"What happened: {feedback.Description}\n" +
            $"Expected result: {feedback.ExpectedResult}\n" +
            $"Sanitized diagnostic events: {(feedback.IncludeSanitizedDiagnostics ? preview.EventCount : 0):N0}\n\n" +
            "PC-SPA includes only these user-entered details and minimum technical context. Personal files, file contents, passwords, browser activity, email addresses, licence keys, cookies, Windows username, computer name, and full personal paths are excluded or redacted.\n\n" +
            "Nothing is sent automatically. Only this reviewed report will be sent securely to the PC-SPA beta feedback service. Continue?";

        return MessageBox.Show(
            message,
            "Preview and send privacy-safe error report",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    public bool ConfirmLocalFeedbackFallback(string submissionFailure) =>
        MessageBox.Show(
            submissionFailure + "\n\n" +
            "PC-SPA can create the same reviewed information as a local ZIP. Nothing will be uploaded. Create the local ZIP now?",
            "Online feedback unavailable",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes) == MessageBoxResult.Yes;

    public bool ConfirmDeleteHistory(int eventCount) =>
        MessageBox.Show(
            $"Delete {eventCount:N0} local diagnostic event(s)?\n\n" +
            "This removes local diagnostic history from this computer. It does not change application settings or cleanup results.",
            "Delete diagnostic history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public bool ConfirmResetInstallationId() =>
        MessageBox.Show(
            "Reset the anonymous local installation ID?\n\n" +
            "PC-SPA will delete existing diagnostic history before creating a new random ID. No account or personal identity is involved.",
            "Reset installation ID",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public void OpenFolder(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);

        Process.Start(new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true
        });
    }

    public void CopyText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Clipboard.SetText(value);
    }
}

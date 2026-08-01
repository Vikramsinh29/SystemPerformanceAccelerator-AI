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

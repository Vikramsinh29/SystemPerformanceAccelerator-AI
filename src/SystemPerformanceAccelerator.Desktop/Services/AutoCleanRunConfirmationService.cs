using System.Windows;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.ViewModels;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class AutoCleanRunConfirmationService :
    IAutoCleanRunConfirmationService
{
    public bool ConfirmCleanup(
        AutoCleanSchedule schedule,
        int selectedFileCount,
        long selectedBytes)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var answer = MessageBox.Show(
            $"Run '{schedule.Name}' now and permanently delete {selectedFileCount:N0} selected previewed temporary file(s)?\n\nEstimated space: {MainWindowViewModel.FormatBytes(selectedBytes)}\n\nOnly the files shown in this fresh preview will be requested. Existing Cleaner safety checks will revalidate every file. This cannot be undone.",
            "Confirm manual Auto Clean run",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return answer == MessageBoxResult.Yes;
    }
}

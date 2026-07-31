using System.Windows;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class StartupItemConfirmationService :
    IStartupItemConfirmationService
{
    public bool ConfirmStateChange(
        StartupItem item,
        StartupItemState requestedState)
    {
        ArgumentNullException.ThrowIfNull(item);

        var action = requestedState == StartupItemState.Enabled
            ? "enable"
            : "disable";
        var actionTitle = requestedState == StartupItemState.Enabled
            ? "Confirm startup enable"
            : "Confirm startup disable";
        var explanation = requestedState == StartupItemState.Enabled
            ? "The original startup command or file will remain unchanged. The target must still be available."
            : "The original startup command or file will not be deleted and can be enabled again later.";

        var answer = MessageBox.Show(
            $"{char.ToUpperInvariant(action[0])}{action[1..]} '{item.Name}' for Windows startup?\n\n{explanation}",
            actionTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return answer == MessageBoxResult.Yes;
    }
}

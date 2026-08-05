using System.Windows;

namespace SystemPerformanceAccelerator.Desktop.Services;

public sealed class AccessInteractionService :
    IAccessInteractionService
{
    public bool ConfirmSignOut() =>
        MessageBox.Show(
            "Sign out of the saved PC-SPA session on this computer?\n\n" +
            "This removes the local session token. Existing local cleanup history and settings stay on this PC.",
            "Sign out of PC-SPA",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public bool ConfirmDeactivateLicense() =>
        MessageBox.Show(
            "Deactivate PC-SPA on this computer?\n\n" +
            "PC-SPA will remove the saved local license token from this Windows user profile. " +
            "If the service cannot be reached, the local token will still be removed and activation will be required again.",
            "Deactivate PC-SPA on this PC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
}

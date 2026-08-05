namespace SystemPerformanceAccelerator.Desktop.Services;

internal sealed class NonInteractiveAccessInteractionService :
    IAccessInteractionService
{
    public bool ConfirmSignOut() => false;

    public bool ConfirmDeactivateLicense() => false;
}

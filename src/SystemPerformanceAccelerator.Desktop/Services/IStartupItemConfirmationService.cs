using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public interface IStartupItemConfirmationService
{
    bool ConfirmStateChange(
        StartupItem item,
        StartupItemState requestedState);
}

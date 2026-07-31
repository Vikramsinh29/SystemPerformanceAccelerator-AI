using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

public interface IAutoCleanRunConfirmationService
{
    bool ConfirmCleanup(
        AutoCleanSchedule schedule,
        int selectedFileCount,
        long selectedBytes);
}

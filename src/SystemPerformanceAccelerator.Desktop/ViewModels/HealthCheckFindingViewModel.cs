using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public enum HealthCheckNavigationTarget
{
    Cleaner,
    LargeFileFinder,
    DuplicateFileFinder,
    SystemMonitor,
    StartupManager
}

public sealed class HealthCheckFindingViewModel
{
    public HealthCheckFindingViewModel(
        HealthCheckItem item,
        HealthRecommendation recommendation,
        Action<HealthCheckFindingViewModel> showRecommendation,
        Action<HealthCheckNavigationTarget> navigate)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        Recommendation = recommendation ??
            throw new ArgumentNullException(nameof(recommendation));
        ArgumentNullException.ThrowIfNull(showRecommendation);
        ArgumentNullException.ThrowIfNull(navigate);

        ViewRecommendationCommand = new RelayCommand(
            () => showRecommendation(this));
        OpenCleanerCommand = new RelayCommand(
            () => navigate(HealthCheckNavigationTarget.Cleaner));
        OpenLargeFileFinderCommand = new RelayCommand(
            () => navigate(HealthCheckNavigationTarget.LargeFileFinder));
        OpenDuplicateFileFinderCommand = new RelayCommand(
            () => navigate(HealthCheckNavigationTarget.DuplicateFileFinder));
        OpenSystemMonitorCommand = new RelayCommand(
            () => navigate(HealthCheckNavigationTarget.SystemMonitor));
        OpenStartupManagerCommand = new RelayCommand(
            () => navigate(HealthCheckNavigationTarget.StartupManager));
    }

    public HealthCheckItem Item { get; }
    public HealthRecommendation Recommendation { get; }

    public RelayCommand ViewRecommendationCommand { get; }
    public RelayCommand OpenCleanerCommand { get; }
    public RelayCommand OpenLargeFileFinderCommand { get; }
    public RelayCommand OpenDuplicateFileFinderCommand { get; }
    public RelayCommand OpenSystemMonitorCommand { get; }
    public RelayCommand OpenStartupManagerCommand { get; }

    public string Name => Item.Name;
    public string Value => Item.Value;
    public string Details => Item.Details;
    public HealthCheckStatus Status => Item.Status;
    public string StatusText => Item.StatusText;

    public bool ShowDiskActions =>
        string.Equals(Name, "System drive", StringComparison.OrdinalIgnoreCase);

    public bool ShowSystemMonitorAction =>
        string.Equals(Name, "Current CPU usage", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Name, "Physical memory", StringComparison.OrdinalIgnoreCase);

    public bool ShowStartupManagerAction =>
        string.Equals(Name, "Startup inventory", StringComparison.OrdinalIgnoreCase);

    public string SafetyNotice => Name switch
    {
        "System drive" =>
            "Review every file before cleanup. Removing the wrong file can affect applications or personal data. Cleaner previews temporary files, while Large File Finder and Duplicate Finder require manual selection before recycling.",
        "Current CPU usage" =>
            "This is one short sample. Confirm sustained activity in System Monitor before closing applications. Do not stop Windows services or unfamiliar processes solely because of this reading.",
        "Physical memory" =>
            "Save work before closing applications. Avoid forced memory-cleaning tools or disabling Windows memory-management features.",
        "Startup inventory" =>
            "Startup entries may belong to security software, drivers, sync tools, or required applications. Research unfamiliar entries before changing them.",
        _ =>
            "Review the finding and understand the impact before making any system change."
    };

    public string AvailableAction => Name switch
    {
        "System drive" =>
            "Choose Cleaner for temporary files, Large File Finder for large items, or Duplicate Finder for content-confirmed copies. Each tool provides its own preview and confirmation.",
        "Current CPU usage" =>
            "Open System Monitor to confirm whether processor load remains high. Close only applications you recognize, using their normal interface.",
        "Physical memory" =>
            "Open System Monitor to observe memory pressure. Save work and close unused applications or browser tabs normally.",
        "Startup inventory" =>
            "Open Startup Manager for a read-only review. Enable or disable actions are not available in the current version.",
        _ =>
            "No direct action is currently available for this finding."
    };
}

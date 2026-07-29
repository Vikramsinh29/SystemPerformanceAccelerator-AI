using System.Windows;
using SystemPerformanceAccelerator.Desktop.ViewModels;
using SystemPerformanceAccelerator.Infrastructure.Services;

namespace SystemPerformanceAccelerator.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var temporaryFileService = new TemporaryFileService();
        var customCleanService = new CustomCleanService(temporaryFileService);
        var largeFileCleanupService = new LargeFileCleanupService();
        var startupItemService = new StartupItemService();
        var systemMonitorService = new SystemMonitorService();
        var healthCheckService = new HealthCheckService(
            systemMonitorService,
            startupItemService);

        DataContext = new MainWindowViewModel(
            temporaryFileService,
            customCleanService,
            new LargeFileService(),
            largeFileCleanupService,
            new DuplicateFileService(),
            new DuplicateFileCleanupService(largeFileCleanupService),
            startupItemService,
            systemMonitorService,
            healthCheckService);
    }
}

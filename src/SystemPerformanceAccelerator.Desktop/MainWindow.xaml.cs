using System.Windows;
using SystemPerformanceAccelerator.Desktop.Services;
using SystemPerformanceAccelerator.Desktop.ViewModels;
using SystemPerformanceAccelerator.Infrastructure.Configuration;
using SystemPerformanceAccelerator.Infrastructure.Services;

namespace SystemPerformanceAccelerator.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        var applicationSettingsService = new ApplicationSettingsService();
        var settingsLoadResult = applicationSettingsService.Load();
        ThemeManager.Apply(settingsLoadResult.Settings.Theme);

        InitializeComponent();

        var temporaryFileService = new TemporaryFileService();
        var customCleanService = new CustomCleanService(temporaryFileService);
        var autoCleanScheduleService = new AutoCleanScheduleService();
        var largeFileCleanupService = new LargeFileCleanupService();
        var startupItemService = new StartupItemService();
        var systemMonitorService = new SystemMonitorService();
        var healthCheckService = new HealthCheckService(
            systemMonitorService,
            startupItemService);

#if DEBUG
        const bool enableDevelopmentEditionOverride = true;
#else
        const bool enableDevelopmentEditionOverride = false;
#endif
        var developmentEditionOverrideProvider =
            new DevelopmentEditionOverrideProvider(
                enableDevelopmentEditionOverride);
        var featureAccessService = new FeatureAccessService(
            EditionFeatureEntitlements.DefaultEdition,
            EditionFeatureEntitlements.Current,
            developmentEditionOverrideProvider);
        var featureAccessGuard = new FeatureAccessGuard(
            featureAccessService);

        DataContext = new MainWindowViewModel(
            temporaryFileService,
            customCleanService,
            autoCleanScheduleService,
            new LargeFileService(),
            largeFileCleanupService,
            new DuplicateFileService(),
            new DuplicateFileCleanupService(largeFileCleanupService),
            startupItemService,
            systemMonitorService,
            healthCheckService,
            applicationSettingsService,
            settingsLoadResult,
            featureAccessGuard);
    }
}

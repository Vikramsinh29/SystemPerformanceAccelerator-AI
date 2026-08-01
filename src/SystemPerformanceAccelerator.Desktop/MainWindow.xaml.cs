using System.Windows;
using System.Windows.Input;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Services;
using SystemPerformanceAccelerator.Desktop.ViewModels;
using SystemPerformanceAccelerator.Infrastructure.Configuration;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;
using SystemPerformanceAccelerator.Infrastructure.Services;

namespace SystemPerformanceAccelerator.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        var settingsService = new ApplicationSettingsService();
        var settingsLoadResult = settingsService.Load();
        var diagnosticService = new LocalDiagnosticService();
        diagnosticService.Configure(
            settingsLoadResult.Settings.LocalDiagnosticsEnabled,
            settingsLoadResult.Settings
                .IncludeHardwareSummaryInDiagnosticExport);

        InitializeWindow(
            settingsService,
            settingsLoadResult,
            diagnosticService,
            new DiagnosticInteractionService());
    }

    internal MainWindow(
        IApplicationSettingsService applicationSettingsService,
        ApplicationSettingsLoadResult settingsLoadResult,
        IDiagnosticService diagnosticService,
        IDiagnosticInteractionService diagnosticInteractionService)
    {
        InitializeWindow(
            applicationSettingsService,
            settingsLoadResult,
            diagnosticService,
            diagnosticInteractionService);
    }

    private void InitializeWindow(
        IApplicationSettingsService applicationSettingsService,
        ApplicationSettingsLoadResult settingsLoadResult,
        IDiagnosticService diagnosticService,
        IDiagnosticInteractionService diagnosticInteractionService)
    {
        ArgumentNullException.ThrowIfNull(
            applicationSettingsService);
        ArgumentNullException.ThrowIfNull(settingsLoadResult);
        ArgumentNullException.ThrowIfNull(diagnosticService);
        ArgumentNullException.ThrowIfNull(
            diagnosticInteractionService);

        ThemeManager.Apply(settingsLoadResult.Settings.Theme);

        InitializeComponent();
        UpdateMaximizeRestoreButton();

        var temporaryFileService = new TemporaryFileService();
        var customCleanService = new CustomCleanService(
            temporaryFileService);
        var autoCleanScheduleService =
            new AutoCleanScheduleService();
        var largeFileCleanupService =
            new LargeFileCleanupService();
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
            new DuplicateFileCleanupService(
                largeFileCleanupService),
            startupItemService,
            systemMonitorService,
            healthCheckService,
            applicationSettingsService,
            settingsLoadResult,
            featureAccessGuard,
            diagnosticService,
            diagnosticInteractionService);
    }

    private void MinimizeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void MaximizeRestoreButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
    }

    private void Window_StateChanged(
        object? sender,
        EventArgs e)
    {
        UpdateMaximizeRestoreButton();
    }

    private void TitleBar_MouseRightButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        var screenPoint = PointToScreen(
            e.GetPosition(this));
        SystemCommands.ShowSystemMenu(this, screenPoint);
    }

    private void UpdateMaximizeRestoreButton()
    {
        if (!IsInitialized ||
            MaximizeRestoreButton is null)
        {
            return;
        }

        var isMaximized =
            WindowState == WindowState.Maximized;
        MaximizeRestoreButton.Content =
            isMaximized ? "\uE923" : "\uE922";
        MaximizeRestoreButton.ToolTip =
            isMaximized ? "Restore down" : "Maximize";
    }
}

using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Services;
using SystemPerformanceAccelerator.Desktop.ViewModels;
using SystemPerformanceAccelerator.Infrastructure.Configuration;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;
using SystemPerformanceAccelerator.Infrastructure.Repairs;
using SystemPerformanceAccelerator.Infrastructure.Services;

namespace SystemPerformanceAccelerator.Desktop;

public partial class MainWindow : Window
{
    private void OnReportErrorClick(
        object sender,
        RoutedEventArgs e)
    {
        BetaErrorFeedbackCard.BringIntoView();
    }

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
            new DiagnosticInteractionService(),
            new DiagnosticFeedbackSubmissionService());
    }

    internal MainWindow(
        IApplicationSettingsService applicationSettingsService,
        ApplicationSettingsLoadResult settingsLoadResult,
        IDiagnosticService diagnosticService,
        IDiagnosticInteractionService diagnosticInteractionService,
        IDiagnosticFeedbackSubmissionService? feedbackSubmissionService = null)
    {
        InitializeWindow(
            applicationSettingsService,
            settingsLoadResult,
            diagnosticService,
            diagnosticInteractionService,
            feedbackSubmissionService ??
                new DisabledDiagnosticFeedbackSubmissionService());
    }

    private void InitializeWindow(
        IApplicationSettingsService applicationSettingsService,
        ApplicationSettingsLoadResult settingsLoadResult,
        IDiagnosticService diagnosticService,
        IDiagnosticInteractionService diagnosticInteractionService,
        IDiagnosticFeedbackSubmissionService feedbackSubmissionService)
    {
        ArgumentNullException.ThrowIfNull(
            applicationSettingsService);
        ArgumentNullException.ThrowIfNull(settingsLoadResult);
        ArgumentNullException.ThrowIfNull(diagnosticService);
        ArgumentNullException.ThrowIfNull(
            diagnosticInteractionService);
        ArgumentNullException.ThrowIfNull(
            feedbackSubmissionService);

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
        var windowsRepairHistoryService =
            new WindowsRepairAssessmentHistoryService();
        var windowsRepairAssessmentService =
            new WindowsRepairAssessmentService(
                new WindowsRepairCommandRunner());
        var windowsRepairPlanService =
            new WindowsRepairPlanService();
        var windowsRepairPlanHistoryService =
            new WindowsRepairPlanHistoryService();
        var windowsRepairExecutionService =
            new WindowsRepairExecutionService(
                new WindowsRepairExecutionCommandRunner(),
                windowsRepairAssessmentService,
                windowsRepairPlanService);
        var windowsRepairExecutionHistoryService =
            new WindowsRepairExecutionHistoryService();
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
        var betaDataRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "SystemPerformanceAccelerator");
        var desktopApiOptions =
            new DesktopApiOptionsProvider().Load();
        var desktopApiClientFactory =
            new DesktopApiHttpClientFactory(desktopApiOptions);
        var desktopApiClient = new DesktopApiClient(
            desktopApiClientFactory.GetOrCreate(),
            desktopApiOptions.Timeout);
        var secureTokenStorage = new FileSecureTokenStorage(
            Path.Combine(
                betaDataRoot,
                "auth",
                "tokens.dat"));
        var authenticationService = new AuthenticationService(
            desktopApiClient,
            secureTokenStorage);
        var licenseActivationService = new LicenseActivationService(
            desktopApiClient,
            secureTokenStorage,
            new WindowsDeviceIdentityProvider(
                Path.Combine(
                    betaDataRoot,
                    "licensing",
                    "device-id.txt")));

        var viewModel = new MainWindowViewModel(
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
            diagnosticInteractionService,
            windowsRepairAssessmentService,
            windowsRepairHistoryService,
            new WindowsRepairAssessmentInteractionService(),
            windowsRepairPlanService,
            windowsRepairPlanHistoryService,
            windowsRepairExecutionService,
            windowsRepairExecutionHistoryService,
            feedbackSubmissionService,
            new AccessInteractionService(),
            authenticationService,
            licenseActivationService,
            secureTokenStorage);
        DataContext = viewModel;
        viewModel.PropertyChanged +=
            OnMainViewModelPropertyChanged;
    }

    private void OnMainViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName !=
            nameof(MainWindowViewModel.ModuleTitle))
        {
            return;
        }

        Dispatcher.BeginInvoke(
            new Action(
                ToolContentScrollViewer.ScrollToTop));
    }

    private void ToolContentScrollViewer_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        var nested = FindNestedScrollViewer(
            e.OriginalSource as DependencyObject);
        if (nested is null || CanScroll(nested, e.Delta))
        {
            return;
        }

        e.Handled = true;
        ToolContentScrollViewer.ScrollToVerticalOffset(
            ToolContentScrollViewer.VerticalOffset - e.Delta);
    }

    private ScrollViewer? FindNestedScrollViewer(
        DependencyObject? current)
    {
        while (current is not null &&
               current != ToolContentScrollViewer)
        {
            if (current is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool CanScroll(
        ScrollViewer scrollViewer,
        int delta) =>
        delta > 0
            ? scrollViewer.VerticalOffset > 0
            : scrollViewer.VerticalOffset <
                scrollViewer.ScrollableHeight;

    private void Window_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !viewModel.WindowsRepairAssessment
                .IsAssessmentRunning)
        {
            return;
        }

        e.Cancel = true;
        MessageBox.Show(
            "A Microsoft Windows assessment or guided repair step is still running.\n\n" +
            "PC-SPA is waiting for the active DISM or SFC process to finish normally. " +
            "Use Stop after current step, keep this window open, and close PC-SPA after the operation finishes.",
            "Windows operation still running",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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

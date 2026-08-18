using System.Windows;
using System.Windows.Threading;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Services;
using SystemPerformanceAccelerator.Infrastructure.Diagnostics;
using SystemPerformanceAccelerator.Infrastructure.Services;

namespace SystemPerformanceAccelerator.Desktop;

public partial class App : Application
{
    private IDiagnosticService? _diagnosticService;
    private bool _isHandlingFatalError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            CommercialUserDataMigrationService.CleanupLegacyBetaAccess();
            var settingsService = new ApplicationSettingsService();
            var settingsLoadResult = settingsService.Load();
            var diagnosticService = new LocalDiagnosticService();
            diagnosticService.Configure(
                settingsLoadResult.Settings.LocalDiagnosticsEnabled,
                settingsLoadResult.Settings
                    .IncludeHardwareSummaryInDiagnosticExport);

            _diagnosticService = diagnosticService;
            RegisterGlobalExceptionHandlers();

            var splashWindow = new SplashWindow();
            splashWindow.Show();

            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() =>
                {
                    try
                    {
                        var mainWindow = new MainWindow(
                            settingsService,
                            settingsLoadResult,
                            diagnosticService,
                            new DiagnosticInteractionService(),
                            new DiagnosticFeedbackSubmissionService());
                        MainWindow = mainWindow;
                        mainWindow.Show();

                        splashWindow.Close();
                        ShutdownMode = ShutdownMode.OnMainWindowClose;
                    }
                    catch (Exception ex)
                    {
                        var reference = TryRecordException(
                            ex,
                            "Application",
                            "Main window startup",
                            recovered: false,
                            userDataMayHaveBeenAffected: false,
                            DiagnosticSeverity.Fatal);

                        splashWindow.Close();
                        ShowFatalError(reference);
                        Shutdown(-1);
                    }
                }));
        }
        catch (Exception ex)
        {
            var reference = TryRecordException(
                ex,
                "Application",
                "Application startup",
                recovered: false,
                userDataMayHaveBeenAffected: false,
                DiagnosticSeverity.Fatal);
            ShowFatalError(reference);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        UnregisterGlobalExceptionHandlers();
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException +=
            OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException +=
            OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException +=
            OnUnobservedTaskException;
    }

    private void UnregisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException -=
            OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -=
            OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -=
            OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        var reference = TryRecordException(
            e.Exception,
            "Application",
            "WPF dispatcher",
            recovered: false,
            userDataMayHaveBeenAffected: false,
            DiagnosticSeverity.Fatal);

        e.Handled = true;
        ShowFatalError(reference);
        Shutdown(-1);
    }

    private void OnAppDomainUnhandledException(
        object? sender,
        UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            TryRecordException(
                exception,
                "Application",
                "AppDomain",
                recovered: false,
                userDataMayHaveBeenAffected: false,
                DiagnosticSeverity.Fatal);
        }
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        TryRecordException(
            e.Exception,
            "Background task",
            "Unobserved task",
            recovered: true,
            userDataMayHaveBeenAffected: false,
            DiagnosticSeverity.Error);
        e.SetObserved();
    }

    private string? TryRecordException(
        Exception exception,
        string feature,
        string operationStage,
        bool recovered,
        bool userDataMayHaveBeenAffected,
        DiagnosticSeverity severity)
    {
        try
        {
            return _diagnosticService?
                .RecordExceptionAsync(
                    exception,
                    feature,
                    operationStage,
                    recovered,
                    userDataMayHaveBeenAffected,
                    severity)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ShowFatalError(string? reference)
    {
        if (_isHandlingFatalError)
        {
            return;
        }

        _isHandlingFatalError = true;
        try
        {
            var referenceText = string.IsNullOrWhiteSpace(reference)
                ? "No local diagnostic reference was created. Local diagnostics may be disabled."
                : $"Error reference: {reference}";

            MessageBox.Show(
                "PC-SPA encountered an unexpected error and must close.\n\n" +
                referenceText +
                "\n\nNo diagnostic information is uploaded automatically.",
                "PC-SPA unexpected error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception)
        {
        }
    }
}

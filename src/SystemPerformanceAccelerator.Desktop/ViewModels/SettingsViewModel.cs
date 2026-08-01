using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;
using SystemPerformanceAccelerator.Desktop.Services;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IApplicationSettingsService _settingsService;
    private readonly IDiagnosticService _diagnosticService;
    private readonly IDiagnosticInteractionService _diagnosticInteractionService;
    private readonly Action<ApplicationSettings> _applySettings;
    private ApplicationTheme _selectedTheme;
    private string _largeFileMinimumSizeText;
    private string _systemMonitorRefreshIntervalText;
    private bool _localDiagnosticsEnabled;
    private bool _includeHardwareSummaryInDiagnosticExport;
    private string _status;

    public SettingsViewModel(
        IApplicationSettingsService settingsService,
        ApplicationSettingsLoadResult loadResult,
        Action<ApplicationSettings> applySettings,
        IFeatureAccessGuard featureAccessGuard,
        IDiagnosticService? diagnosticService = null,
        IDiagnosticInteractionService? diagnosticInteractionService = null)
    {
        _settingsService = settingsService ??
            throw new ArgumentNullException(nameof(settingsService));
        ArgumentNullException.ThrowIfNull(loadResult);
        _applySettings = applySettings ??
            throw new ArgumentNullException(nameof(applySettings));
        ArgumentNullException.ThrowIfNull(featureAccessGuard);
        _diagnosticService = diagnosticService ??
            new DisabledDiagnosticService();
        _diagnosticInteractionService =
            diagnosticInteractionService ??
            new NonInteractiveDiagnosticInteractionService();

        _selectedTheme = loadResult.Settings.Theme;
        _largeFileMinimumSizeText =
            loadResult.Settings.LargeFileMinimumSizeMb.ToString();
        _systemMonitorRefreshIntervalText =
            loadResult.Settings.SystemMonitorRefreshIntervalSeconds.ToString();
        _localDiagnosticsEnabled =
            loadResult.Settings.LocalDiagnosticsEnabled;
        _includeHardwareSummaryInDiagnosticExport =
            loadResult.Settings.IncludeHardwareSummaryInDiagnosticExport;
        _status = loadResult.HasWarning
            ? loadResult.Warning
            : loadResult.Settings.LocalDiagnosticsEnabled &&
              !_diagnosticService.IsEnabled
                ? "Settings were loaded, but local diagnostics could not be enabled. Check access to the local application-data folder."
                : "Settings are stored locally on this computer.";

        SaveCommand = new RelayCommand(
            Save,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        RestoreDefaultsCommand = new RelayCommand(
            RestoreDefaults,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        OpenDiagnosticsFolderCommand = new RelayCommand(
            OpenDiagnosticsFolder,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        ExportDiagnosticPackageCommand = new AsyncRelayCommand(
            ExportDiagnosticPackageAsync,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        CopyLatestErrorReferenceCommand = new RelayCommand(
            CopyLatestErrorReference,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        DeleteDiagnosticHistoryCommand = new RelayCommand(
            DeleteDiagnosticHistory,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
        ResetInstallationIdCommand = new RelayCommand(
            ResetInstallationId,
            featureAccessGuard,
            ApplicationFeature.Settings,
            FeatureAccessRequirement.Execute);
    }

    public IReadOnlyList<ApplicationTheme> ThemeOptions { get; } =
        Enum.GetValues<ApplicationTheme>();

    public RelayCommand SaveCommand { get; }

    public RelayCommand RestoreDefaultsCommand { get; }

    public RelayCommand OpenDiagnosticsFolderCommand { get; }

    public AsyncRelayCommand ExportDiagnosticPackageCommand { get; }

    public RelayCommand CopyLatestErrorReferenceCommand { get; }

    public RelayCommand DeleteDiagnosticHistoryCommand { get; }

    public RelayCommand ResetInstallationIdCommand { get; }

    public ApplicationTheme SelectedTheme
    {
        get => _selectedTheme;
        set => SetField(ref _selectedTheme, value);
    }

    public string LargeFileMinimumSizeText
    {
        get => _largeFileMinimumSizeText;
        set => SetField(ref _largeFileMinimumSizeText, value);
    }

    public string SystemMonitorRefreshIntervalText
    {
        get => _systemMonitorRefreshIntervalText;
        set => SetField(
            ref _systemMonitorRefreshIntervalText,
            value);
    }

    public bool LocalDiagnosticsEnabled
    {
        get => _localDiagnosticsEnabled;
        set
        {
            if (SetField(ref _localDiagnosticsEnabled, value))
            {
                OnPropertyChanged(nameof(DiagnosticsEnabledStatus));
            }
        }
    }

    public bool IncludeHardwareSummaryInDiagnosticExport
    {
        get => _includeHardwareSummaryInDiagnosticExport;
        set
        {
            if (SetField(
                    ref _includeHardwareSummaryInDiagnosticExport,
                    value))
            {
                OnPropertyChanged(nameof(DiagnosticExportPolicyText));
            }
        }
    }

    public bool ConfirmBeforeCleanup => true;

    public string SettingsPath => _settingsService.SettingsPath;

    public string DiagnosticsPath =>
        _diagnosticService.DiagnosticsRoot;

    public string DiagnosticsEnabledStatus =>
        LocalDiagnosticsEnabled == _diagnosticService.IsEnabled
            ? _diagnosticService.IsEnabled
                ? "Enabled • local-only • no automatic upload"
                : "Disabled • no crash records are being created"
            : "Pending save • current diagnostic state has not changed";

    public string InstallationIdText =>
        _diagnosticService.InstallationId ??
        "Not created. Enable and save local diagnostics first.";

    public string LatestErrorReferenceText =>
        _diagnosticService.LatestErrorReference ??
        "No local error reference is available.";

    public string DiagnosticExportPolicyText =>
        IncludeHardwareSummaryInDiagnosticExport
            ? "After saving, manual exports will include a basic CPU and memory summary."
            : "Manual exports exclude the optional CPU and memory summary.";

    public string DiagnosticVersionText
    {
        get
        {
            var environment = _diagnosticService.CurrentEnvironment;
            return $"Version {environment.ApplicationVersion} • build {environment.BuildIdentifier}";
        }
    }

    public string DiagnosticWindowsText =>
        _diagnosticService.CurrentEnvironment.WindowsVersion;

    public string DiagnosticRuntimeText =>
        _diagnosticService.CurrentEnvironment.RuntimeVersion;

    public string DiagnosticElevationText =>
        _diagnosticService.CurrentEnvironment.IsElevated
            ? "Administrator / elevated"
            : "Standard / not elevated";

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    private void Save()
    {
        if (!TryCreateSettings(out var settings, out var error))
        {
            Status = error;
            return;
        }

        try
        {
            _settingsService.Save(settings);
            _applySettings(settings);
            RefreshDiagnosticProperties();
            Status = settings.LocalDiagnosticsEnabled
                ? _diagnosticService.IsEnabled
                    ? "Settings saved. Privacy-safe local diagnostics are enabled; nothing is uploaded automatically."
                    : "Settings saved, but local diagnostics could not be enabled. Check access to the local application-data folder."
                : "Settings saved. Local diagnostics are disabled.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            Status =
                "Settings could not be saved. Existing settings remain active. " +
                ex.Message;
        }
    }

    private void RestoreDefaults()
    {
        var defaults = ApplicationSettings.Default;
        SelectedTheme = defaults.Theme;
        LargeFileMinimumSizeText =
            defaults.LargeFileMinimumSizeMb.ToString();
        SystemMonitorRefreshIntervalText =
            defaults.SystemMonitorRefreshIntervalSeconds.ToString();
        LocalDiagnosticsEnabled =
            defaults.LocalDiagnosticsEnabled;
        IncludeHardwareSummaryInDiagnosticExport =
            defaults.IncludeHardwareSummaryInDiagnosticExport;

        try
        {
            _settingsService.Save(defaults);
            _applySettings(defaults);
            RefreshDiagnosticProperties();
            Status =
                "Safe default settings restored and applied. Existing diagnostic history was not deleted.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            Status =
                "Defaults could not be saved. Existing settings remain active. " +
                ex.Message;
        }
    }

    private void OpenDiagnosticsFolder()
    {
        try
        {
            _diagnosticInteractionService.OpenFolder(
                _diagnosticService.DiagnosticsRoot);
            Status = "Opened the local diagnostics folder.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentException or
            Win32Exception or
            System.Security.SecurityException)
        {
            Status =
                "The diagnostics folder could not be opened. " +
                ex.Message;
        }
    }

    private async Task ExportDiagnosticPackageAsync()
    {
        if (!_diagnosticService.IsEnabled)
        {
            Status =
                "Enable local diagnostics and save Settings before exporting a package.";
            return;
        }

        try
        {
            var preview = _diagnosticService.CreateExportPreview();
            if (!_diagnosticInteractionService.ConfirmExport(preview))
            {
                Status = "Diagnostic export not started.";
                return;
            }

            var suggestedName =
                $"PC-SPA-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            var destination =
                _diagnosticInteractionService.SelectExportPath(
                    suggestedName);

            if (string.IsNullOrWhiteSpace(destination))
            {
                Status = "Diagnostic export cancelled.";
                return;
            }

            var result = await _diagnosticService.ExportAsync(
                destination);
            Status = result.Message;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException or
            System.Security.SecurityException)
        {
            Status =
                "The diagnostic package could not be exported. " +
                ex.Message;
        }
    }

    private void CopyLatestErrorReference()
    {
        var reference = _diagnosticService.LatestErrorReference;
        if (string.IsNullOrWhiteSpace(reference))
        {
            Status = "No local error reference is available to copy.";
            return;
        }

        try
        {
            _diagnosticInteractionService.CopyText(reference);
            Status = $"Copied error reference {reference}.";
        }
        catch (ExternalException ex)
        {
            Status =
                "The error reference could not be copied to the clipboard. " +
                ex.Message;
        }
    }

    private void DeleteDiagnosticHistory()
    {
        var preview = _diagnosticService.CreateExportPreview();
        if (!_diagnosticInteractionService.ConfirmDeleteHistory(
                preview.EventCount))
        {
            Status = "Diagnostic history was not deleted.";
            return;
        }

        try
        {
            _diagnosticService.DeleteHistory();
            RefreshDiagnosticProperties();
            Status =
                "Local diagnostic history deleted. The anonymous installation ID was retained.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            Status =
                "Diagnostic history could not be deleted. " +
                ex.Message;
        }
    }

    private void ResetInstallationId()
    {
        if (!_diagnosticInteractionService.ConfirmResetInstallationId())
        {
            Status = "Installation ID was not reset.";
            return;
        }

        try
        {
            _diagnosticService.ResetInstallationId();
            RefreshDiagnosticProperties();
            Status = _diagnosticService.IsEnabled
                ? "Diagnostic history deleted and a new anonymous installation ID created."
                : "Diagnostic history and the previous anonymous installation ID deleted.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException or
            System.Security.SecurityException)
        {
            Status =
                "The anonymous installation ID could not be reset. " +
                ex.Message;
        }
    }

    private bool TryCreateSettings(
        out ApplicationSettings settings,
        out string error)
    {
        settings = ApplicationSettings.Default;

        if (!int.TryParse(
                LargeFileMinimumSizeText,
                out var minimumSize) ||
            minimumSize <
                ApplicationSettings.MinimumLargeFileSizeMb ||
            minimumSize >
                ApplicationSettings.MaximumLargeFileSizeMb)
        {
            error =
                $"Large File Finder default must be a whole number from {ApplicationSettings.MinimumLargeFileSizeMb:N0} to {ApplicationSettings.MaximumLargeFileSizeMb:N0} MB.";
            return false;
        }

        if (!int.TryParse(
                SystemMonitorRefreshIntervalText,
                out var refreshSeconds) ||
            refreshSeconds <
                ApplicationSettings.MinimumMonitorRefreshSeconds ||
            refreshSeconds >
                ApplicationSettings.MaximumMonitorRefreshSeconds)
        {
            error =
                $"System Monitor refresh interval must be a whole number from {ApplicationSettings.MinimumMonitorRefreshSeconds} to {ApplicationSettings.MaximumMonitorRefreshSeconds} seconds.";
            return false;
        }

        settings = new ApplicationSettings(
            SelectedTheme,
            true,
            minimumSize,
            refreshSeconds)
        {
            LocalDiagnosticsEnabled =
                LocalDiagnosticsEnabled,
            IncludeHardwareSummaryInDiagnosticExport =
                IncludeHardwareSummaryInDiagnosticExport
        };
        error = string.Empty;
        return true;
    }

    private void RefreshDiagnosticProperties()
    {
        OnPropertyChanged(nameof(DiagnosticsEnabledStatus));
        OnPropertyChanged(nameof(InstallationIdText));
        OnPropertyChanged(nameof(LatestErrorReferenceText));
        OnPropertyChanged(nameof(DiagnosticExportPolicyText));
        OnPropertyChanged(nameof(DiagnosticVersionText));
        OnPropertyChanged(nameof(DiagnosticWindowsText));
        OnPropertyChanged(nameof(DiagnosticRuntimeText));
        OnPropertyChanged(nameof(DiagnosticElevationText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(name));
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;
using System.IO;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IApplicationSettingsService _settingsService;
    private readonly Action<ApplicationSettings> _applySettings;
    private ApplicationTheme _selectedTheme;
    private string _largeFileMinimumSizeText;
    private string _systemMonitorRefreshIntervalText;
    private string _status;

    public SettingsViewModel(
        IApplicationSettingsService settingsService,
        ApplicationSettingsLoadResult loadResult,
        Action<ApplicationSettings> applySettings,
        IFeatureAccessGuard featureAccessGuard)
    {
        _settingsService = settingsService ??
            throw new ArgumentNullException(nameof(settingsService));
        ArgumentNullException.ThrowIfNull(loadResult);
        _applySettings = applySettings ??
            throw new ArgumentNullException(nameof(applySettings));
        ArgumentNullException.ThrowIfNull(featureAccessGuard);

        _selectedTheme = loadResult.Settings.Theme;
        _largeFileMinimumSizeText =
            loadResult.Settings.LargeFileMinimumSizeMb.ToString();
        _systemMonitorRefreshIntervalText =
            loadResult.Settings.SystemMonitorRefreshIntervalSeconds.ToString();
        _status = loadResult.HasWarning
            ? loadResult.Warning
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
    }

    public IReadOnlyList<ApplicationTheme> ThemeOptions { get; } =
        Enum.GetValues<ApplicationTheme>();

    public RelayCommand SaveCommand { get; }
    public RelayCommand RestoreDefaultsCommand { get; }

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
        set => SetField(ref _systemMonitorRefreshIntervalText, value);
    }

    public bool ConfirmBeforeCleanup => true;

    public string SettingsPath => _settingsService.SettingsPath;

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
            Status = "Settings saved locally and applied.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            Status = $"Settings could not be saved. Existing settings remain active. {ex.Message}";
        }
    }

    private void RestoreDefaults()
    {
        var defaults = ApplicationSettings.Default;
        SelectedTheme = defaults.Theme;
        LargeFileMinimumSizeText = defaults.LargeFileMinimumSizeMb.ToString();
        SystemMonitorRefreshIntervalText =
            defaults.SystemMonitorRefreshIntervalSeconds.ToString();

        try
        {
            _settingsService.Save(defaults);
            _applySettings(defaults);
            Status = "Safe default settings restored and applied.";
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            Status = $"Defaults could not be saved. Existing settings remain active. {ex.Message}";
        }
    }

    private bool TryCreateSettings(
        out ApplicationSettings settings,
        out string error)
    {
        settings = ApplicationSettings.Default;

        if (!int.TryParse(LargeFileMinimumSizeText, out var minimumSize) ||
            minimumSize < ApplicationSettings.MinimumLargeFileSizeMb ||
            minimumSize > ApplicationSettings.MaximumLargeFileSizeMb)
        {
            error = $"Large File Finder default must be a whole number from {ApplicationSettings.MinimumLargeFileSizeMb:N0} to {ApplicationSettings.MaximumLargeFileSizeMb:N0} MB.";
            return false;
        }

        if (!int.TryParse(SystemMonitorRefreshIntervalText, out var refreshSeconds) ||
            refreshSeconds < ApplicationSettings.MinimumMonitorRefreshSeconds ||
            refreshSeconds > ApplicationSettings.MaximumMonitorRefreshSeconds)
        {
            error = $"System Monitor refresh interval must be a whole number from {ApplicationSettings.MinimumMonitorRefreshSeconds} to {ApplicationSettings.MaximumMonitorRefreshSeconds} seconds.";
            return false;
        }

        settings = new ApplicationSettings(
            SelectedTheme,
            true,
            minimumSize,
            refreshSeconds);
        error = string.Empty;
        return true;
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

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

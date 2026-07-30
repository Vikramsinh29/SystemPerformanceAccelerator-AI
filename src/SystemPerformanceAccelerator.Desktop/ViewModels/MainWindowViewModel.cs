using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;
using SystemPerformanceAccelerator.Desktop.Services;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private enum ApplicationModule
    {
        Cleaner,
        HealthCheck,
        CustomClean,
        LargeFileFinder,
        DuplicateFileFinder,
        StartupManager,
        SystemMonitor,
        Settings
    }

    private readonly ITemporaryFileService _temporaryFileService;
    private readonly IFeatureAccessGuard _featureAccessGuard;
    private CancellationTokenSource? _cancellationTokenSource;
    private ApplicationModule _currentModule = ApplicationModule.Cleaner;
    private bool _isBusy;
    private int _progress;
    private string _status = "Ready. Scan before cleaning anything.";
    private string _scanStatus = "Not scanned";
    private OperationResultPresentation _operationResult = OperationResultPresentation.Hidden;

    public MainWindowViewModel(
        ITemporaryFileService temporaryFileService,
        ICustomCleanService customCleanService,
        ILargeFileService largeFileService,
        ILargeFileCleanupService largeFileCleanupService,
        IDuplicateFileService duplicateFileService,
        IDuplicateFileCleanupService duplicateFileCleanupService,
        IStartupItemService startupItemService,
        ISystemMonitorService systemMonitorService,
        IHealthCheckService healthCheckService,
        IApplicationSettingsService applicationSettingsService,
        ApplicationSettingsLoadResult settingsLoadResult,
        IFeatureAccessGuard featureAccessGuard)
    {
        _temporaryFileService = temporaryFileService;
        _featureAccessGuard = featureAccessGuard ??
            throw new ArgumentNullException(nameof(featureAccessGuard));

        CleanerAccess = CreateAccess(ApplicationFeature.Cleaner);
        HealthCheckAccess = CreateAccess(ApplicationFeature.HealthCheck);
        CustomCleanAccess = CreateAccess(ApplicationFeature.CustomClean);
        LargeFileFinderAccess = CreateAccess(ApplicationFeature.LargeFileFinder);
        DuplicateFileFinderAccess = CreateAccess(ApplicationFeature.DuplicateFileFinder);
        StartupManagerAccess = CreateAccess(ApplicationFeature.StartupManager);
        SystemMonitorAccess = CreateAccess(ApplicationFeature.SystemMonitor);
        SettingsAccess = CreateAccess(ApplicationFeature.Settings);
        _currentModule = GetInitialModule();

        HealthCheck = new HealthCheckViewModel(
            healthCheckService,
            OpenHealthCheckTarget,
            featureAccessGuard);
        HealthCheck.PropertyChanged += OnChildModulePropertyChanged;
        CustomClean = new CustomCleanViewModel(
            customCleanService,
            featureAccessGuard);
        CustomClean.PropertyChanged += OnChildModulePropertyChanged;
        LargeFileFinder = new LargeFileFinderViewModel(
            largeFileService,
            largeFileCleanupService,
            featureAccessGuard,
            settingsLoadResult.Settings.LargeFileMinimumSizeMb);
        LargeFileFinder.PropertyChanged += OnChildModulePropertyChanged;
        DuplicateFileFinder = new DuplicateFileFinderViewModel(
            duplicateFileService,
            duplicateFileCleanupService,
            featureAccessGuard);
        DuplicateFileFinder.PropertyChanged += OnChildModulePropertyChanged;
        StartupManager = new StartupManagerViewModel(
            startupItemService,
            featureAccessGuard);
        StartupManager.PropertyChanged += OnChildModulePropertyChanged;
        SystemMonitor = new SystemMonitorViewModel(
            systemMonitorService,
            featureAccessGuard,
            settingsLoadResult.Settings.SystemMonitorRefreshIntervalSeconds);
        Settings = new SettingsViewModel(
            applicationSettingsService,
            settingsLoadResult,
            ApplySettings,
            featureAccessGuard);

        ScanCommand = new AsyncRelayCommand(
            ScanAsync,
            featureAccessGuard,
            ApplicationFeature.Cleaner,
            FeatureAccessRequirement.Execute,
            () => !IsBusy);
        CleanCommand = new AsyncRelayCommand(
            CleanAsync,
            featureAccessGuard,
            ApplicationFeature.Cleaner,
            FeatureAccessRequirement.Execute,
            () => !IsBusy && Candidates.Any(x => x.IsSelected));
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        ShowCleanerCommand = CreateNavigationCommand(ApplicationModule.Cleaner);
        ShowHealthCheckCommand = CreateNavigationCommand(ApplicationModule.HealthCheck);
        ShowCustomCleanCommand = CreateNavigationCommand(ApplicationModule.CustomClean);
        ShowLargeFileFinderCommand = CreateNavigationCommand(ApplicationModule.LargeFileFinder);
        ShowDuplicateFileFinderCommand = CreateNavigationCommand(ApplicationModule.DuplicateFileFinder);
        ShowStartupManagerCommand = CreateNavigationCommand(ApplicationModule.StartupManager);
        ShowSystemMonitorCommand = CreateNavigationCommand(ApplicationModule.SystemMonitor);
        ShowSettingsCommand = CreateNavigationCommand(ApplicationModule.Settings);
    }

    public ObservableCollection<CleanupCandidateViewModel> Candidates { get; } = [];
    public HealthCheckViewModel HealthCheck { get; }
    public CustomCleanViewModel CustomClean { get; }
    public LargeFileFinderViewModel LargeFileFinder { get; }
    public DuplicateFileFinderViewModel DuplicateFileFinder { get; }
    public StartupManagerViewModel StartupManager { get; }
    public SystemMonitorViewModel SystemMonitor { get; }
    public SettingsViewModel Settings { get; }
    public FeatureAccessPresentation CleanerAccess { get; }
    public FeatureAccessPresentation HealthCheckAccess { get; }
    public FeatureAccessPresentation CustomCleanAccess { get; }
    public FeatureAccessPresentation LargeFileFinderAccess { get; }
    public FeatureAccessPresentation DuplicateFileFinderAccess { get; }
    public FeatureAccessPresentation StartupManagerAccess { get; }
    public FeatureAccessPresentation SystemMonitorAccess { get; }
    public FeatureAccessPresentation SettingsAccess { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ShowCleanerCommand { get; }
    public RelayCommand ShowHealthCheckCommand { get; }
    public RelayCommand ShowCustomCleanCommand { get; }
    public RelayCommand ShowLargeFileFinderCommand { get; }
    public RelayCommand ShowDuplicateFileFinderCommand { get; }
    public RelayCommand ShowStartupManagerCommand { get; }
    public RelayCommand ShowSystemMonitorCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }

    public bool IsCleanerActive => _currentModule == ApplicationModule.Cleaner;
    public bool IsHealthCheckActive => _currentModule == ApplicationModule.HealthCheck;
    public bool IsCustomCleanActive => _currentModule == ApplicationModule.CustomClean;
    public bool IsLargeFileFinderActive => _currentModule == ApplicationModule.LargeFileFinder;
    public bool IsDuplicateFileFinderActive => _currentModule == ApplicationModule.DuplicateFileFinder;
    public bool IsStartupManagerActive => _currentModule == ApplicationModule.StartupManager;
    public bool IsSystemMonitorActive => _currentModule == ApplicationModule.SystemMonitor;
    public bool IsSettingsActive => _currentModule == ApplicationModule.Settings;

    public bool IsCleanerContentVisible => IsCleanerActive && CleanerAccess.IsAvailable;
    public bool IsHealthCheckContentVisible => IsHealthCheckActive && HealthCheckAccess.IsAvailable;
    public bool IsCustomCleanContentVisible => IsCustomCleanActive && CustomCleanAccess.IsAvailable;
    public bool IsLargeFileFinderContentVisible => IsLargeFileFinderActive && LargeFileFinderAccess.IsAvailable;
    public bool IsDuplicateFileFinderContentVisible => IsDuplicateFileFinderActive && DuplicateFileFinderAccess.IsAvailable;
    public bool IsStartupManagerContentVisible => IsStartupManagerActive && StartupManagerAccess.IsAvailable;
    public bool IsSystemMonitorContentVisible => IsSystemMonitorActive && SystemMonitorAccess.IsAvailable;
    public bool IsSettingsContentVisible => IsSettingsActive && SettingsAccess.IsAvailable;

    public FeatureAccessPresentation CurrentFeatureAccess =>
        GetAccessPresentation(_currentModule);

    public bool IsLockedFeatureActive =>
        CurrentFeatureAccess.IsVisible && !CurrentFeatureAccess.IsAvailable;

    public string EditionStatusText
    {
        get
        {
            var editionName = ApplicationEditionHierarchy.GetDisplayName(
                _featureAccessGuard.EffectiveEdition);
            return _featureAccessGuard.IsDevelopmentOverrideActive
                ? $"{editionName} edition • local development override"
                : $"{editionName} edition • local system utility";
        }
    }

    public string ModuleTitle => _currentModule switch
    {
        ApplicationModule.Cleaner => "Cleaner",
        ApplicationModule.HealthCheck => "Health Check",
        ApplicationModule.CustomClean => "Custom Clean",
        ApplicationModule.LargeFileFinder => "Large File Finder",
        ApplicationModule.DuplicateFileFinder => "Duplicate File Finder",
        ApplicationModule.StartupManager => "Startup Manager",
        ApplicationModule.SystemMonitor => "System Monitor",
        ApplicationModule.Settings => "Settings",
        _ => "System Performance Accelerator"
    };

    public string ModuleSubtitle => _currentModule switch
    {
        ApplicationModule.Cleaner => "Safely review and remove temporary files",
        ApplicationModule.HealthCheck => "Review key system conditions without changing Windows",
        ApplicationModule.CustomClean => "Preview and safely clean selected existing Cleaner categories",
        ApplicationModule.LargeFileFinder => "Find and safely move selected large files to the Windows Recycle Bin",
        ApplicationModule.DuplicateFileFinder => "Find content-confirmed duplicates and safely recycle selected copies",
        ApplicationModule.StartupManager => "Review Windows startup entries without changing them",
        ApplicationModule.SystemMonitor => "View live total CPU and physical-memory usage without changing the system",
        ApplicationModule.Settings => "Manage local appearance and safe operating defaults",
        _ => string.Empty
    };

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
            {
                return;
            }

            ScanCommand.RaiseCanExecuteChanged();
            CleanCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            RaiseNavigationCanExecuteChanged();
        }
    }

    public int Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string ScanStatus
    {
        get => _scanStatus;
        private set => SetField(ref _scanStatus, value);
    }

    public OperationResultPresentation OperationResult
    {
        get => _operationResult;
        private set => SetField(ref _operationResult, value);
    }

    public string FilesFound => Candidates.Count.ToString("N0");
    public string ReclaimableSpace => FormatBytes(Candidates.Sum(x => x.Model.SizeBytes));
    public string Summary => $"{FilesFound} files • {ReclaimableSpace}";

    private void SwitchModule(ApplicationModule module)
    {
        var feature = GetFeature(module);
        if (_currentModule == module ||
            !CanSwitchModule() ||
            !_featureAccessGuard.CanAccess(
                feature,
                FeatureAccessRequirement.Navigate))
        {
            return;
        }

        if (_currentModule == ApplicationModule.SystemMonitor &&
            module != ApplicationModule.SystemMonitor)
        {
            SystemMonitor.StopMonitoring();
        }

        _currentModule = module;
        RaiseModulePropertiesChanged();
    }

    private RelayCommand CreateNavigationCommand(ApplicationModule module) =>
        new(
            () => SwitchModule(module),
            _featureAccessGuard,
            GetFeature(module),
            FeatureAccessRequirement.Navigate,
            CanSwitchModule);

    private FeatureAccessPresentation CreateAccess(ApplicationFeature feature) =>
        new(_featureAccessGuard.GetAccess(feature));

    private FeatureAccessPresentation GetAccessPresentation(
        ApplicationModule module) => module switch
        {
            ApplicationModule.Cleaner => CleanerAccess,
            ApplicationModule.HealthCheck => HealthCheckAccess,
            ApplicationModule.CustomClean => CustomCleanAccess,
            ApplicationModule.LargeFileFinder => LargeFileFinderAccess,
            ApplicationModule.DuplicateFileFinder => DuplicateFileFinderAccess,
            ApplicationModule.StartupManager => StartupManagerAccess,
            ApplicationModule.SystemMonitor => SystemMonitorAccess,
            ApplicationModule.Settings => SettingsAccess,
            _ => CleanerAccess
        };

    private ApplicationModule GetInitialModule()
    {
        foreach (var module in Enum.GetValues<ApplicationModule>())
        {
            if (GetAccessPresentation(module).IsVisible)
            {
                return module;
            }
        }

        return ApplicationModule.Cleaner;
    }

    private static ApplicationFeature GetFeature(ApplicationModule module) =>
        module switch
        {
            ApplicationModule.Cleaner => ApplicationFeature.Cleaner,
            ApplicationModule.HealthCheck => ApplicationFeature.HealthCheck,
            ApplicationModule.CustomClean => ApplicationFeature.CustomClean,
            ApplicationModule.LargeFileFinder => ApplicationFeature.LargeFileFinder,
            ApplicationModule.DuplicateFileFinder => ApplicationFeature.DuplicateFileFinder,
            ApplicationModule.StartupManager => ApplicationFeature.StartupManager,
            ApplicationModule.SystemMonitor => ApplicationFeature.SystemMonitor,
            ApplicationModule.Settings => ApplicationFeature.Settings,
            _ => (ApplicationFeature)(-1)
        };

    private void RaiseModulePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsCleanerActive));
        OnPropertyChanged(nameof(IsHealthCheckActive));
        OnPropertyChanged(nameof(IsCustomCleanActive));
        OnPropertyChanged(nameof(IsLargeFileFinderActive));
        OnPropertyChanged(nameof(IsDuplicateFileFinderActive));
        OnPropertyChanged(nameof(IsStartupManagerActive));
        OnPropertyChanged(nameof(IsSystemMonitorActive));
        OnPropertyChanged(nameof(IsSettingsActive));
        OnPropertyChanged(nameof(IsCleanerContentVisible));
        OnPropertyChanged(nameof(IsHealthCheckContentVisible));
        OnPropertyChanged(nameof(IsCustomCleanContentVisible));
        OnPropertyChanged(nameof(IsLargeFileFinderContentVisible));
        OnPropertyChanged(nameof(IsDuplicateFileFinderContentVisible));
        OnPropertyChanged(nameof(IsStartupManagerContentVisible));
        OnPropertyChanged(nameof(IsSystemMonitorContentVisible));
        OnPropertyChanged(nameof(IsSettingsContentVisible));
        OnPropertyChanged(nameof(CurrentFeatureAccess));
        OnPropertyChanged(nameof(IsLockedFeatureActive));
        OnPropertyChanged(nameof(ModuleTitle));
        OnPropertyChanged(nameof(ModuleSubtitle));
    }

    private void OpenHealthCheckTarget(HealthCheckNavigationTarget target)
    {
        var module = target switch
        {
            HealthCheckNavigationTarget.Cleaner => ApplicationModule.Cleaner,
            HealthCheckNavigationTarget.LargeFileFinder => ApplicationModule.LargeFileFinder,
            HealthCheckNavigationTarget.DuplicateFileFinder => ApplicationModule.DuplicateFileFinder,
            HealthCheckNavigationTarget.SystemMonitor => ApplicationModule.SystemMonitor,
            HealthCheckNavigationTarget.StartupManager => ApplicationModule.StartupManager,
            _ => ApplicationModule.HealthCheck
        };

        SwitchModule(module);
    }

    private bool CanSwitchModule() =>
        !IsBusy &&
        !HealthCheck.IsBusy &&
        !CustomClean.IsBusy &&
        !LargeFileFinder.IsBusy &&
        !DuplicateFileFinder.IsBusy &&
        !StartupManager.IsBusy;

    private void OnChildModulePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LargeFileFinderViewModel.IsBusy))
        {
            RaiseNavigationCanExecuteChanged();
        }
    }

    private void RaiseNavigationCanExecuteChanged()
    {
        ShowCleanerCommand.RaiseCanExecuteChanged();
        ShowHealthCheckCommand.RaiseCanExecuteChanged();
        ShowCustomCleanCommand.RaiseCanExecuteChanged();
        ShowLargeFileFinderCommand.RaiseCanExecuteChanged();
        ShowDuplicateFileFinderCommand.RaiseCanExecuteChanged();
        ShowStartupManagerCommand.RaiseCanExecuteChanged();
        ShowSystemMonitorCommand.RaiseCanExecuteChanged();
        ShowSettingsCommand.RaiseCanExecuteChanged();
    }

    private void ApplySettings(ApplicationSettings settings)
    {
        ThemeManager.Apply(settings.Theme);
        LargeFileFinder.ApplyDefaultMinimumSize(settings.LargeFileMinimumSizeMb);
        SystemMonitor.ApplyRefreshInterval(
            settings.SystemMonitorRefreshIntervalSeconds);
    }

    private async Task ScanAsync()
    {
        BeginOperation("Scanning the current user's temporary folder...");
        ScanStatus = "Scanning...";

        try
        {
            var result = await _temporaryFileService.ScanAsync(
                new Progress<int>(value => Progress = value),
                _cancellationTokenSource!.Token);

            Candidates.Clear();
            foreach (var candidate in result.Candidates)
            {
                var viewModel = new CleanupCandidateViewModel(candidate);
                viewModel.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(CleanupCandidateViewModel.IsSelected))
                    {
                        CleanCommand.RaiseCanExecuteChanged();
                    }
                };
                Candidates.Add(viewModel);
            }

            RefreshSummary();
            var elapsed = FormatElapsed(result.Elapsed);
            ScanStatus = result.Errors.Count == 0
                ? $"Completed • {elapsed}"
                : $"Completed • {elapsed} • {result.Errors.Count} skipped";
            Status = result.Errors.Count == 0
                ? $"Scan complete in {elapsed}. Review the list before cleaning."
                : $"Scan complete in {elapsed} with {result.Errors.Count} skipped item(s).";
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Cancelled";
            Status = "Scan cancelled. No files were deleted.";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task CleanAsync()
    {
        var selected = Candidates.Where(x => x.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            Status = "Select at least one file.";
            return;
        }

        var size = selected.Sum(x => x.Model.SizeBytes);
        var answer = MessageBox.Show(
            $"Delete {selected.Length:N0} selected temporary file(s) and attempt to reclaim {FormatBytes(size)}?\n\nThis cannot be undone.",
            "Confirm cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            Status = "Cleanup not started.";
            return;
        }

        BeginOperation("Cleaning selected temporary files...");
        try
        {
            var result = await _temporaryFileService.CleanAsync(
                selected.Select(x => x.Model).ToArray(),
                new Progress<int>(value => Progress = value),
                _cancellationTokenSource!.Token);

            var deletedPaths = selected.Select(x => x.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Candidates.Where(x => deletedPaths.Contains(x.FullPath) && !File.Exists(x.FullPath)).ToArray())
            {
                Candidates.Remove(item);
            }

            RefreshSummary();
            var elapsed = FormatElapsed(result.Elapsed);
            OperationResult = new OperationResultPresentation(
                true,
                "DELETED",
                result.DeletedCount.ToString("N0"),
                result.Errors.Count.ToString("N0"),
                "0",
                FormatBytes(result.ReclaimedBytes),
                elapsed,
                result.Errors.Count > 0 ? result.Errors[0] : string.Empty);
            Status = result.CompletedWithoutErrors
                ? "Cleanup completed successfully."
                : "Cleanup completed with skipped items.";
        }
        catch (OperationCanceledException)
        {
            RefreshSummary();
            Status = "Cleanup cancelled. Files already deleted remain deleted; remaining files were untouched.";
        }
        finally
        {
            EndOperation();
        }
    }

    private void BeginOperation(string status)
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        Progress = 0;
        OperationResult = OperationResultPresentation.Hidden;
        Status = status;
        IsBusy = true;
    }

    private void EndOperation()
    {
        IsBusy = false;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(FilesFound));
        OnPropertyChanged(nameof(ReclaimableSpace));
        OnPropertyChanged(nameof(Summary));
    }

    private void Cancel() => _cancellationTokenSource?.Cancel();

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds < 1)
        {
            return "<1 ms";
        }

        if (elapsed.TotalSeconds < 1)
        {
            return $"{elapsed.TotalMilliseconds:0} ms";
        }

        return $"{elapsed.TotalSeconds:0.0} s";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
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

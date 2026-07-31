using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;
using SystemPerformanceAccelerator.Desktop.Services;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class StartupManagerViewModel : INotifyPropertyChanged
{
    private readonly IStartupItemService _startupItemService;
    private readonly IStartupItemConfirmationService _confirmationService;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isBusy;
    private int _progress;
    private string _status = "Scan Windows startup locations, then click an Enabled or Disabled status to change that item safely.";
    private string _scanStatus = "Not scanned";
    private string _progressText = "0 locations checked";

    public StartupManagerViewModel(
        IStartupItemService startupItemService,
        IFeatureAccessGuard featureAccessGuard)
        : this(
            startupItemService,
            featureAccessGuard,
            new StartupItemConfirmationService())
    {
    }

    public StartupManagerViewModel(
        IStartupItemService startupItemService,
        IFeatureAccessGuard featureAccessGuard,
        IStartupItemConfirmationService confirmationService)
    {
        _startupItemService = startupItemService ??
            throw new ArgumentNullException(nameof(startupItemService));
        _confirmationService = confirmationService ??
            throw new ArgumentNullException(nameof(confirmationService));
        ArgumentNullException.ThrowIfNull(featureAccessGuard);

        ScanCommand = new AsyncRelayCommand(
            ScanAsync,
            featureAccessGuard,
            ApplicationFeature.StartupManager,
            FeatureAccessRequirement.Execute,
            () => !IsBusy);
        ToggleItemStateCommand = new AsyncParameterCommand(
            ToggleItemStateAsync,
            featureAccessGuard,
            ApplicationFeature.StartupManager,
            FeatureAccessRequirement.Execute,
            CanToggleItemState);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public ObservableCollection<StartupItemRowViewModel> Results { get; } = [];

    public AsyncRelayCommand ScanCommand { get; }

    public AsyncParameterCommand ToggleItemStateCommand { get; }

    public RelayCommand CancelCommand { get; }

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
            ToggleItemStateCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
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

    public string ProgressText
    {
        get => _progressText;
        private set => SetField(ref _progressText, value);
    }

    public string ItemsFound => Results.Count.ToString("N0");

    public string EnabledItems => Results
        .Count(item => item.State == StartupItemState.Enabled)
        .ToString("N0");

    public string DisabledItems => Results
        .Count(item => item.State == StartupItemState.Disabled)
        .ToString("N0");

    public string UnknownItems => Results
        .Count(item => item.State == StartupItemState.Unknown)
        .ToString("N0");

    private async Task ScanAsync()
    {
        BeginOperation(
            "Reading supported Windows startup locations. No entry will be changed during the scan.",
            "Starting startup scan...",
            "Scanning...");

        try
        {
            var result = await LoadResultsAsync();
            CompleteScan(result, "Scan complete");
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Cancelled";
            Status = "Startup scan cancelled. No startup settings were changed.";
        }
        catch (Exception ex)
        {
            ScanStatus = "Failed";
            Status = $"Startup scan failed safely. No startup settings were changed. {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task ToggleItemStateAsync(object? parameter)
    {
        if (parameter is not StartupItemRowViewModel row)
        {
            Status = "The selected startup row is no longer available. Run a fresh scan.";
            return;
        }

        var item = row.Model;
        var requestedState = row.RequestedState;
        if (requestedState == StartupItemState.Unknown || !row.CanToggle)
        {
            Status = string.IsNullOrWhiteSpace(item.StateChangeUnavailableReason)
                ? "This startup item cannot be changed safely."
                : item.StateChangeUnavailableReason;
            return;
        }

        var action = requestedState == StartupItemState.Enabled
            ? "enable"
            : "disable";

        if (!_confirmationService.ConfirmStateChange(item, requestedState))
        {
            Status = $"Startup {action} not started.";
            return;
        }

        row.IsUpdating = true;
        ToggleItemStateCommand.RaiseCanExecuteChanged();
        BeginOperation(
            $"Requesting Windows to {action} '{item.Name}' without deleting its startup entry...",
            "Validating the scanned startup entry...",
            "Updating...");

        try
        {
            var changeResult = await _startupItemService.SetStateAsync(
                item,
                requestedState,
                _cancellationTokenSource!.Token);

            if (!changeResult.Succeeded)
            {
                Progress = 100;
                ScanStatus = changeResult.Outcome switch
                {
                    StartupItemStateChangeOutcome.Stale => "Fresh scan required",
                    StartupItemStateChangeOutcome.AccessDenied => "Permission denied",
                    StartupItemStateChangeOutcome.Unsupported => "Not supported",
                    _ => "Update failed"
                };
                ProgressText = "No startup command or file was deleted";
                Status = changeResult.Message;
                return;
            }

            Status = $"{changeResult.Message} Refreshing the startup inventory...";
            ProgressText = "Refreshing startup locations...";
            var scanResult = await LoadResultsAsync();
            CompleteScan(
                scanResult,
                changeResult.StateChanged ? "Updated" : "Already current");
            Status = $"{changeResult.Message} Inventory refreshed: {scanResult.Items.Count:N0} startup item(s).";
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Cancelled";
            Status = "Startup operation cancelled. Run a fresh scan to confirm the current Windows state.";
            ProgressText = "No startup command or file was deleted";
        }
        catch (Exception ex)
        {
            ScanStatus = "Update failed";
            Status = $"Startup state update failed safely. Run a fresh scan before trying again. {ex.Message}";
            ProgressText = "No startup command or file was deleted";
        }
        finally
        {
            if (Results.Contains(row))
            {
                row.IsUpdating = false;
            }

            EndOperation();
        }
    }

    private bool CanToggleItemState(object? parameter) =>
        !IsBusy &&
        parameter is StartupItemRowViewModel row &&
        row.CanToggle;

    private async Task<StartupItemScanResult> LoadResultsAsync()
    {
        ClearResults();
        RefreshSummary();

        var progress = new Progress<StartupItemScanProgress>(value =>
        {
            Progress = value.TotalLocations == 0
                ? 100
                : (int)Math.Round(
                    value.LocationsScanned * 100d /
                    value.TotalLocations);
            ProgressText = $"{value.LocationsScanned:N0} of {value.TotalLocations:N0} locations checked";
        });

        var result = await _startupItemService.ScanAsync(
            progress,
            _cancellationTokenSource!.Token);

        foreach (var item in result.Items)
        {
            Results.Add(new StartupItemRowViewModel(item));
        }

        Progress = 100;
        ProgressText = $"{result.LocationsScanned:N0} locations checked";
        RefreshSummary();
        ToggleItemStateCommand.RaiseCanExecuteChanged();
        return result;
    }

    private void CompleteScan(
        StartupItemScanResult result,
        string completionLabel)
    {
        var elapsed = FormatElapsed(result.Elapsed);
        ScanStatus = result.Errors.Count == 0
            ? $"{completionLabel} - {elapsed}"
            : $"{completionLabel} - {elapsed} - {result.Errors.Count:N0} issue(s)";
        Status = result.Errors.Count == 0
            ? $"Startup inventory ready. Found {result.Items.Count:N0} item(s). Click an Enabled or Disabled status to change that row. Original entries are never deleted."
            : $"Startup inventory completed with {result.Errors.Count:N0} issue(s). First issue: {result.Errors[0]}";
    }

    private void BeginOperation(
        string status,
        string progressText,
        string scanStatus)
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        Progress = 0;
        ProgressText = progressText;
        ScanStatus = scanStatus;
        Status = status;
        IsBusy = true;
    }

    private void EndOperation()
    {
        IsBusy = false;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void Cancel() => _cancellationTokenSource?.Cancel();

    private void ClearResults()
    {
        Results.Clear();
        ToggleItemStateCommand.RaiseCanExecuteChanged();
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(ItemsFound));
        OnPropertyChanged(nameof(EnabledItems));
        OnPropertyChanged(nameof(DisabledItems));
        OnPropertyChanged(nameof(UnknownItems));
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

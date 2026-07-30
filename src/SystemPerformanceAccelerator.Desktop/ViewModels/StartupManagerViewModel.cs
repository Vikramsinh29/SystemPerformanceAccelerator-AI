using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class StartupManagerViewModel : INotifyPropertyChanged
{
    private readonly IStartupItemService _startupItemService;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isBusy;
    private int _progress;
    private string _status = "Scan Windows startup locations. This module never changes startup items.";
    private string _scanStatus = "Not scanned";
    private string _progressText = "0 locations checked";

    public StartupManagerViewModel(
        IStartupItemService startupItemService,
        IFeatureAccessGuard featureAccessGuard)
    {
        _startupItemService = startupItemService;
        ArgumentNullException.ThrowIfNull(featureAccessGuard);
        ScanCommand = new AsyncRelayCommand(
            ScanAsync,
            featureAccessGuard,
            ApplicationFeature.StartupManager,
            FeatureAccessRequirement.Execute,
            () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public ObservableCollection<StartupItem> Results { get; } = [];
    public AsyncRelayCommand ScanCommand { get; }
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

    private async Task ScanAsync()
    {
        BeginOperation();
        Results.Clear();
        RefreshSummary();

        try
        {
            var progress = new Progress<StartupItemScanProgress>(value =>
            {
                Progress = value.TotalLocations == 0
                    ? 100
                    : (int)Math.Round(value.LocationsScanned * 100d / value.TotalLocations);
                ProgressText = $"{value.LocationsScanned:N0} of {value.TotalLocations:N0} locations checked";
            });

            var result = await _startupItemService.ScanAsync(
                progress,
                _cancellationTokenSource!.Token);

            foreach (var item in result.Items)
            {
                Results.Add(item);
            }

            Progress = 100;
            ProgressText = $"{result.LocationsScanned:N0} locations checked";
            RefreshSummary();

            var elapsed = FormatElapsed(result.Elapsed);
            ScanStatus = result.Errors.Count == 0
                ? $"Completed - {elapsed}"
                : $"Completed - {elapsed} - {result.Errors.Count:N0} issue(s)";
            Status = result.Errors.Count == 0
                ? $"Read-only scan complete. Found {result.Items.Count:N0} startup item(s)."
                : $"Read-only scan complete with {result.Errors.Count:N0} issue(s). First issue: {result.Errors[0]}";
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

    private void BeginOperation()
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        Progress = 0;
        ProgressText = "Starting read-only scan...";
        ScanStatus = "Scanning...";
        Status = "Reading startup locations. No startup settings will be changed.";
        IsBusy = true;
    }

    private void EndOperation()
    {
        IsBusy = false;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void Cancel() => _cancellationTokenSource?.Cancel();

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(ItemsFound));
        OnPropertyChanged(nameof(EnabledItems));
        OnPropertyChanged(nameof(DisabledItems));
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

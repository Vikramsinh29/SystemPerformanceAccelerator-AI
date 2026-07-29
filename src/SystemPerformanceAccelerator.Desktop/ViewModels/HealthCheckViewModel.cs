using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class HealthCheckViewModel : INotifyPropertyChanged
{
    private readonly IHealthCheckService _healthCheckService;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isBusy;
    private bool _hasResults;
    private HealthCheckStatus _overallStatus = HealthCheckStatus.Unknown;
    private int _warningCount;
    private string _status =
        "Run a read-only health check to review drive space, CPU, memory, and startup items.";
    private string _lastChecked = "Not checked";

    public HealthCheckViewModel(IHealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService ??
            throw new ArgumentNullException(nameof(healthCheckService));
        RunCheckCommand = new AsyncRelayCommand(RunCheckAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public ObservableCollection<HealthCheckItem> Results { get; } = [];
    public AsyncRelayCommand RunCheckCommand { get; }
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

            RunCheckCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CheckState));
        }
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string LastChecked
    {
        get => _lastChecked;
        private set => SetField(ref _lastChecked, value);
    }

    public int GoodCount =>
        Results.Count(item => item.Status == HealthCheckStatus.Good);

    public int AttentionCount =>
        Results.Count(item => item.Status == HealthCheckStatus.Attention);

    public int UnknownCount =>
        Results.Count(item => item.Status == HealthCheckStatus.Unknown);

    public string OverallStatusText => !_hasResults
        ? "Not checked"
        : _overallStatus switch
        {
            HealthCheckStatus.Good => "Good",
            HealthCheckStatus.Attention => "Attention",
            _ => "Unknown"
        };

    public string CheckState => IsBusy ? "Checking" : "Ready";
    public string WarningText => $"{_warningCount:N0} warning(s)";

    private async Task RunCheckAsync()
    {
        BeginCheck();

        try
        {
            var result = await _healthCheckService.RunAsync(
                _cancellationTokenSource!.Token);

            Results.Clear();
            foreach (var item in result.Items)
            {
                Results.Add(item);
            }

            _hasResults = true;
            _overallStatus = result.OverallStatus;
            _warningCount = result.Errors.Count;
            LastChecked = result.CompletedAt.ToLocalTime().ToString("HH:mm:ss");
            Status = result.Errors.Count == 0
                ? "Read-only health check completed. No system settings were changed."
                : $"Health check completed with {result.Errors.Count:N0} warning(s). No system settings were changed.";
            RaiseSummaryProperties();
        }
        catch (OperationCanceledException)
        {
            Status = "Health check cancelled. No system settings were changed.";
            LastChecked = "Cancelled";
        }
        catch (Exception ex)
        {
            Status = $"Health check failed safely. No system settings were changed. {ex.Message}";
            LastChecked = "Failed";
        }
        finally
        {
            EndCheck();
        }
    }

    private void BeginCheck()
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        Results.Clear();
        _hasResults = false;
        _overallStatus = HealthCheckStatus.Unknown;
        _warningCount = 0;
        LastChecked = "In progress";
        Status = "Running read-only health checks...";
        RaiseSummaryProperties();
        IsBusy = true;
    }

    private void EndCheck()
    {
        IsBusy = false;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void Cancel() => _cancellationTokenSource?.Cancel();

    private void RaiseSummaryProperties()
    {
        OnPropertyChanged(nameof(GoodCount));
        OnPropertyChanged(nameof(AttentionCount));
        OnPropertyChanged(nameof(UnknownCount));
        OnPropertyChanged(nameof(OverallStatusText));
        OnPropertyChanged(nameof(WarningText));
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

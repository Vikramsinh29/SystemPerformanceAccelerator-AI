using System.ComponentModel;
using System.Runtime.CompilerServices;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class SystemMonitorViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan CpuSampleDuration = TimeSpan.FromMilliseconds(500);

    private readonly ISystemMonitorService _systemMonitorService;
    private TimeSpan _refreshInterval;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isMonitoring;
    private double _cpuUsagePercent;
    private double _memoryUsagePercent;
    private long _usedMemoryBytes;
    private long _availableMemoryBytes;
    private long _totalMemoryBytes;
    private string _status = "Start monitoring to view live total CPU and physical-memory usage.";
    private string _lastUpdated = "Not started";

    public SystemMonitorViewModel(
        ISystemMonitorService systemMonitorService,
        IFeatureAccessGuard featureAccessGuard,
        int refreshIntervalSeconds = 1)
    {
        _systemMonitorService = systemMonitorService;
        ArgumentNullException.ThrowIfNull(featureAccessGuard);
        _refreshInterval = TimeSpan.FromSeconds(Math.Clamp(refreshIntervalSeconds, 1, 10));
        StartCommand = new AsyncRelayCommand(
            MonitorAsync,
            featureAccessGuard,
            ApplicationFeature.SystemMonitor,
            FeatureAccessRequirement.Execute,
            () => !IsMonitoring);
        StopCommand = new RelayCommand(StopMonitoring, () => IsMonitoring);
    }

    public AsyncRelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        private set
        {
            if (!SetField(ref _isMonitoring, value))
            {
                return;
            }

            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(MonitoringState));
        }
    }

    public double CpuUsagePercent
    {
        get => _cpuUsagePercent;
        private set
        {
            if (SetField(ref _cpuUsagePercent, value))
            {
                OnPropertyChanged(nameof(CpuUsageText));
            }
        }
    }

    public double MemoryUsagePercent
    {
        get => _memoryUsagePercent;
        private set
        {
            if (SetField(ref _memoryUsagePercent, value))
            {
                OnPropertyChanged(nameof(MemoryUsageText));
            }
        }
    }

    public long UsedMemoryBytes
    {
        get => _usedMemoryBytes;
        private set
        {
            if (SetField(ref _usedMemoryBytes, value))
            {
                OnPropertyChanged(nameof(UsedMemoryText));
            }
        }
    }

    public long AvailableMemoryBytes
    {
        get => _availableMemoryBytes;
        private set
        {
            if (SetField(ref _availableMemoryBytes, value))
            {
                OnPropertyChanged(nameof(AvailableMemoryText));
            }
        }
    }

    public long TotalMemoryBytes
    {
        get => _totalMemoryBytes;
        private set
        {
            if (SetField(ref _totalMemoryBytes, value))
            {
                OnPropertyChanged(nameof(TotalMemoryText));
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string LastUpdated
    {
        get => _lastUpdated;
        private set => SetField(ref _lastUpdated, value);
    }

    public string CpuUsageText => $"{CpuUsagePercent:0.0}%";
    public string MemoryUsageText => $"{MemoryUsagePercent:0.0}%";
    public string UsedMemoryText => FormatBytes(UsedMemoryBytes);
    public string AvailableMemoryText => FormatBytes(AvailableMemoryBytes);
    public string TotalMemoryText => FormatBytes(TotalMemoryBytes);
    public string MonitoringState => IsMonitoring ? "Monitoring" : "Stopped";

    public void ApplyRefreshInterval(int refreshIntervalSeconds)
    {
        _refreshInterval = TimeSpan.FromSeconds(
            Math.Clamp(refreshIntervalSeconds, 1, 10));
        Status = $"Refresh interval set to {_refreshInterval.TotalSeconds:0} second(s). Start monitoring when ready.";
    }

    public void StopMonitoring()
    {
        _cancellationTokenSource?.Cancel();
    }

    private async Task MonitorAsync()
    {
        BeginMonitoring();

        try
        {
            while (true)
            {
                var snapshot = await _systemMonitorService.CaptureAsync(
                    CpuSampleDuration,
                    _cancellationTokenSource!.Token);

                ApplySnapshot(snapshot);
                Status = "Live read-only monitoring is active. No system settings are being changed.";

                var remainingDelay = _refreshInterval - CpuSampleDuration;
                if (remainingDelay > TimeSpan.Zero)
                {
                    await Task.Delay(
                        remainingDelay,
                        _cancellationTokenSource.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Status = "Monitoring stopped. No system settings were changed.";
        }
        catch (Exception ex)
        {
            Status = $"Monitoring failed safely. No system settings were changed. {ex.Message}";
        }
        finally
        {
            EndMonitoring();
        }
    }

    private void BeginMonitoring()
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        Status = "Starting read-only CPU and memory monitoring...";
        IsMonitoring = true;
    }

    private void EndMonitoring()
    {
        IsMonitoring = false;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void ApplySnapshot(SystemMonitorSnapshot snapshot)
    {
        CpuUsagePercent = snapshot.CpuUsagePercent;
        MemoryUsagePercent = snapshot.MemoryUsagePercent;
        UsedMemoryBytes = snapshot.UsedPhysicalMemoryBytes;
        AvailableMemoryBytes = snapshot.AvailablePhysicalMemoryBytes;
        TotalMemoryBytes = snapshot.TotalPhysicalMemoryBytes;
        LastUpdated = snapshot.CapturedAt.ToLocalTime().ToString("HH:mm:ss");
    }

    private static string FormatBytes(long bytes)
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

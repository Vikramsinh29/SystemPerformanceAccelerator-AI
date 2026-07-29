using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class LargeFileFinderViewModel : INotifyPropertyChanged
{
    private readonly ILargeFileService _largeFileService;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isBusy;
    private int _progress;
    private string _selectedFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _minimumSizeText = "100";
    private string _status = "Choose a folder or drive, set the minimum size, and start a read-only scan.";
    private string _scanStatus = "Not scanned";
    private string _progressText = "0 files checked";

    public LargeFileFinderViewModel(ILargeFileService largeFileService)
    {
        _largeFileService = largeFileService;
        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public ObservableCollection<LargeFileCandidate> Results { get; } = [];
    public RelayCommand BrowseCommand { get; }
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

            BrowseCommand.RaiseCanExecuteChanged();
            ScanCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public int Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    public string SelectedFolder
    {
        get => _selectedFolder;
        set => SetField(ref _selectedFolder, value);
    }

    public string MinimumSizeText
    {
        get => _minimumSizeText;
        set => SetField(ref _minimumSizeText, value);
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

    public string FilesFound => Results.Count.ToString("N0");
    public string TotalSize => MainWindowViewModel.FormatBytes(Results.Sum(result => result.SizeBytes));

    private void Browse()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder or drive to scan",
            InitialDirectory = Directory.Exists(SelectedFolder)
                ? SelectedFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedFolder = dialog.FolderName;
            Status = "Location selected. Start Scan when ready.";
        }
    }

    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFolder))
        {
            Status = "Choose a folder or drive before scanning.";
            return;
        }

        if (!long.TryParse(MinimumSizeText, out var minimumSizeMb) || minimumSizeMb <= 0)
        {
            Status = "Minimum size must be a whole number greater than 0 MB.";
            return;
        }

        const long bytesPerMegabyte = 1024L * 1024L;
        if (minimumSizeMb > long.MaxValue / bytesPerMegabyte)
        {
            Status = "The minimum size value is too large.";
            return;
        }

        BeginOperation();
        Results.Clear();
        RefreshSummary();

        try
        {
            var progress = new Progress<LargeFileScanProgress>(value =>
            {
                ProgressText = $"{value.FilesScanned:N0} files checked - {value.DirectoriesScanned:N0} folders";
            });

            var result = await _largeFileService.ScanAsync(
                SelectedFolder,
                minimumSizeMb * bytesPerMegabyte,
                progress,
                _cancellationTokenSource!.Token);

            foreach (var candidate in result.Candidates)
            {
                Results.Add(candidate);
            }

            Progress = 100;
            RefreshSummary();

            var elapsed = FormatElapsed(result.Elapsed);
            ProgressText = $"{result.FilesScanned:N0} files checked - {result.DirectoriesScanned:N0} folders";
            ScanStatus = result.Errors.Count == 0
                ? $"Completed - {elapsed}"
                : $"Completed - {elapsed} - {result.Errors.Count:N0} skipped";

            Status = result.Errors.Count == 0
                ? $"Scan complete. Found {result.Candidates.Count:N0} file(s) at least {minimumSizeMb:N0} MB. No files were changed."
                : $"Scan complete with {result.Errors.Count:N0} skipped item(s). First issue: {result.Errors[0]}";
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Cancelled";
            Status = "Large-file scan cancelled. No files were changed.";
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
        ProgressText = "Starting scan...";
        ScanStatus = "Scanning...";
        Status = "Scanning the selected location. This operation is read-only.";
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
        OnPropertyChanged(nameof(FilesFound));
        OnPropertyChanged(nameof(TotalSize));
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


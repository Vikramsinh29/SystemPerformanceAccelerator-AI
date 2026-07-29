using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class DuplicateFileFinderViewModel : INotifyPropertyChanged
{
    private readonly IDuplicateFileService _duplicateFileService;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isBusy;
    private int _progress;
    private int _groupsFound;
    private long _potentialReclaimableBytes;
    private string _selectedFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _status = "Choose a folder or drive and start a read-only duplicate scan.";
    private string _scanStatus = "Not scanned";
    private string _progressText = "0 files checked";

    public DuplicateFileFinderViewModel(IDuplicateFileService duplicateFileService)
    {
        _duplicateFileService = duplicateFileService;

        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public ObservableCollection<DuplicateFileCandidateViewModel> Results { get; } = [];
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

    public string GroupsFound => _groupsFound.ToString("N0");
    public string DuplicateFiles => Results.Count.ToString("N0");
    public string PotentialReclaimableSpace => MainWindowViewModel.FormatBytes(_potentialReclaimableBytes);

    private void Browse()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder or drive to scan for duplicates",
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

        BeginOperation();
        ClearResults();

        try
        {
            var progress = new Progress<DuplicateFileScanProgress>(value =>
            {
                if (value.Phase == DuplicateFileScanPhase.DiscoveringFiles)
                {
                    ProgressText = $"{value.FilesScanned:N0} files checked - {value.DirectoriesScanned:N0} folders";
                    return;
                }

                Progress = value.HashCandidateCount == 0
                    ? 100
                    : (int)Math.Round(value.HashCandidatesProcessed * 100d / value.HashCandidateCount);
                ProgressText = $"{value.HashCandidatesProcessed:N0} of {value.HashCandidateCount:N0} candidate files hashed";
            });

            var result = await _duplicateFileService.ScanAsync(
                SelectedFolder,
                progress,
                _cancellationTokenSource!.Token);

            var groupNumber = 1;
            foreach (var group in result.Groups)
            {
                foreach (var candidate in group.Files)
                {
                    Results.Add(new DuplicateFileCandidateViewModel(
                        candidate,
                        groupNumber,
                        group.Files.Count,
                        group.ReclaimableBytes));
                }

                groupNumber++;
            }

            _groupsFound = result.Groups.Count;
            _potentialReclaimableBytes = result.PotentialReclaimableBytes;
            Progress = 100;
            RefreshSummary();

            var elapsed = FormatElapsed(result.Elapsed);
            ProgressText = $"{result.FilesScanned:N0} files checked - {result.FilesHashed:N0} files hashed";
            ScanStatus = result.Errors.Count == 0
                ? $"Completed - {elapsed}"
                : $"Completed - {elapsed} - {result.Errors.Count:N0} skipped";

            if (result.Groups.Count == 0)
            {
                Status = result.Errors.Count == 0
                    ? "Scan complete. No SHA-256 content-confirmed duplicates were found. No files were changed."
                    : $"Scan complete with {result.Errors.Count:N0} skipped item(s) and no confirmed duplicates. First issue: {result.Errors[0]}";
            }
            else
            {
                Status = result.Errors.Count == 0
                    ? $"Scan complete. Found {result.Groups.Count:N0} duplicate group(s) containing {result.DuplicateFileCount:N0} files. Results are SHA-256 content-confirmed and read-only."
                    : $"Scan complete with {result.Errors.Count:N0} skipped item(s). Found {result.Groups.Count:N0} confirmed duplicate group(s). First issue: {result.Errors[0]}";
            }
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Cancelled";
            Status = "Duplicate scan cancelled. No files were changed.";
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
        Status = "Scanning by file size, then verifying matching sizes with SHA-256. No files will be changed.";
        IsBusy = true;
    }

    private void EndOperation()
    {
        IsBusy = false;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void ClearResults()
    {
        Results.Clear();
        _groupsFound = 0;
        _potentialReclaimableBytes = 0;
        RefreshSummary();
    }

    private void Cancel() => _cancellationTokenSource?.Cancel();

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(GroupsFound));
        OnPropertyChanged(nameof(DuplicateFiles));
        OnPropertyChanged(nameof(PotentialReclaimableSpace));
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

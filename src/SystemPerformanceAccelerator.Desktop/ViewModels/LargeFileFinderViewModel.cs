using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class LargeFileFinderViewModel : INotifyPropertyChanged
{
    private readonly ILargeFileService _largeFileService;
    private readonly ILargeFileCleanupService _largeFileCleanupService;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isBusy;
    private int _progress;
    private string _selectedFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _minimumSizeText;
    private string _status = "Choose a folder or drive, set the minimum size, and start a scan.";
    private string _scanStatus = "Not scanned";
    private string _progressText = "0 files checked";
    private OperationResultPresentation _operationResult = OperationResultPresentation.Hidden;

    public LargeFileFinderViewModel(
        ILargeFileService largeFileService,
        ILargeFileCleanupService largeFileCleanupService,
        IFeatureAccessGuard featureAccessGuard,
        int defaultMinimumSizeMb = 100)
    {
        _largeFileService = largeFileService;
        _largeFileCleanupService = largeFileCleanupService;
        ArgumentNullException.ThrowIfNull(featureAccessGuard);
        _minimumSizeText = Math.Max(1, defaultMinimumSizeMb).ToString();

        BrowseCommand = new RelayCommand(
            Browse,
            featureAccessGuard,
            ApplicationFeature.LargeFileFinder,
            FeatureAccessRequirement.Execute,
            () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(
            ScanAsync,
            featureAccessGuard,
            ApplicationFeature.LargeFileFinder,
            FeatureAccessRequirement.Execute,
            () => !IsBusy);
        DeleteSelectedCommand = new AsyncRelayCommand(
            DeleteSelectedAsync,
            featureAccessGuard,
            ApplicationFeature.LargeFileFinder,
            FeatureAccessRequirement.Execute,
            () => !IsBusy && Results.Any(result => result.IsSelected));
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public ObservableCollection<LargeFileCandidateViewModel> Results { get; } = [];
    public RelayCommand BrowseCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand DeleteSelectedCommand { get; }
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
            DeleteSelectedCommand.RaiseCanExecuteChanged();
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

    public OperationResultPresentation OperationResult
    {
        get => _operationResult;
        private set => SetField(ref _operationResult, value);
    }

    public string FilesFound => Results.Count.ToString("N0");
    public string TotalSize => MainWindowViewModel.FormatBytes(Results.Sum(result => result.Model.SizeBytes));

    public bool? AreAllResultsSelected
    {
        get => BulkSelection.GetState(Results, item => item.IsSelected);
        set
        {
            var targetSelection = BulkSelection.ResolveTarget(
                value,
                AreAllResultsSelected);
            if (targetSelection is null)
            {
                return;
            }

            BulkSelection.SetAll(
                Results,
                targetSelection.Value,
                static (item, isSelected) => item.IsSelected = isSelected);
            OnPropertyChanged();
            DeleteSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    public void ApplyDefaultMinimumSize(int minimumSizeMb)
    {
        if (IsBusy)
        {
            return;
        }

        MinimumSizeText = Math.Max(1, minimumSizeMb).ToString();
        Status = "The saved default minimum size has been applied.";
    }

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

        BeginOperation(
            "Scanning the selected location. No files will be changed.",
            "Starting scan...",
            "Scanning...");
        ClearResults();
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
                var viewModel = new LargeFileCandidateViewModel(candidate);
                viewModel.PropertyChanged += OnCandidatePropertyChanged;
                Results.Add(viewModel);
            }

            Progress = 100;
            RefreshSummary();

            var elapsed = FormatElapsed(result.Elapsed);
            ProgressText = $"{result.FilesScanned:N0} files checked - {result.DirectoriesScanned:N0} folders";
            ScanStatus = result.Errors.Count == 0
                ? $"Completed - {elapsed}"
                : $"Completed - {elapsed} - {result.Errors.Count:N0} skipped";

            Status = result.Errors.Count == 0
                ? $"Scan complete. Found {result.Candidates.Count:N0} file(s) at least {minimumSizeMb:N0} MB. Select only files you recognize before deletion."
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

    private async Task DeleteSelectedAsync()
    {
        var selected = Results.Where(result => result.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            Status = "Select at least one file.";
            return;
        }

        var selectedBytes = selected.Sum(result => result.Model.SizeBytes);
        var answer = MessageBox.Show(
            $"Move {selected.Length:N0} selected file(s) ({MainWindowViewModel.FormatBytes(selectedBytes)}) to the Windows Recycle Bin?\n\nProtected, locked, missing, or inaccessible files will be skipped. Recycled files can normally be restored from Windows Recycle Bin.",
            "Confirm large-file cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            Status = "Large-file cleanup not started.";
            return;
        }

        BeginOperation(
            "Moving selected files to the Windows Recycle Bin...",
            $"0 of {selected.Length:N0} processed",
            "Cleaning...");

        try
        {
            var progress = new Progress<LargeFileCleanupProgress>(value =>
            {
                Progress = value.TotalCount == 0
                    ? 100
                    : (int)Math.Round(value.ProcessedCount * 100d / value.TotalCount);
                ProgressText = $"{value.ProcessedCount:N0} of {value.TotalCount:N0} processed";
            });

            var result = await _largeFileCleanupService.CleanAsync(
                SelectedFolder,
                selected.Select(item => item.Model).ToArray(),
                progress,
                _cancellationTokenSource!.Token);

            var recycledPaths = result.RecycledPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Results.Where(item => recycledPaths.Contains(item.FullPath)).ToArray())
            {
                item.PropertyChanged -= OnCandidatePropertyChanged;
                Results.Remove(item);
            }

            Progress = 100;
            RefreshSummary();
            DeleteSelectedCommand.RaiseCanExecuteChanged();

            var elapsed = FormatElapsed(result.Elapsed);
            ScanStatus = result.CompletedWithoutErrors
                ? $"Cleanup completed - {elapsed}"
                : $"Cleanup completed - {elapsed} - {result.SkippedCount:N0} skipped";

            OperationResult = new OperationResultPresentation(
                true,
                "RECYCLED",
                result.RecycledCount.ToString("N0"),
                result.SkippedCount.ToString("N0"),
                "0",
                MainWindowViewModel.FormatBytes(result.ReclaimedBytes),
                elapsed,
                result.Errors.Count > 0 ? result.Errors[0] : string.Empty);
            Status = result.CompletedWithoutErrors
                ? "Large-file cleanup completed successfully."
                : "Large-file cleanup completed with skipped items.";
        }
        catch (OperationCanceledException)
        {
            RefreshSummary();
            ScanStatus = "Cleanup cancelled";
            Status = "Cleanup cancelled. Files already moved to the Recycle Bin remain there; remaining files were untouched.";
        }
        finally
        {
            EndOperation();
        }
    }

    private void OnCandidatePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LargeFileCandidateViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(AreAllResultsSelected));
            DeleteSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    private void ClearResults()
    {
        foreach (var result in Results)
        {
            result.PropertyChanged -= OnCandidatePropertyChanged;
        }

        Results.Clear();
        DeleteSelectedCommand.RaiseCanExecuteChanged();
    }

    private void BeginOperation(string status, string progressText, string scanStatus)
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        Progress = 0;
        OperationResult = OperationResultPresentation.Hidden;
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

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(FilesFound));
        OnPropertyChanged(nameof(TotalSize));
        OnPropertyChanged(nameof(AreAllResultsSelected));
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

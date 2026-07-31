using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class DuplicateFileFinderViewModel : INotifyPropertyChanged
{
    private readonly IDuplicateFileService _duplicateFileService;
    private readonly IDuplicateFileCleanupService _duplicateFileCleanupService;
    private CancellationTokenSource? _cancellationTokenSource;
    private IReadOnlyList<DuplicateFileGroup> _confirmedGroups = [];
    private bool _isBusy;
    private bool _isCorrectingSelection;
    private int _progress;
    private string _selectedFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _status = "Choose a folder or drive, scan for duplicates, then manually select copies to recycle.";
    private string _scanStatus = "Not scanned";
    private string _progressText = "0 files checked";
    private OperationResultPresentation _operationResult = OperationResultPresentation.Hidden;

    public DuplicateFileFinderViewModel(
        IDuplicateFileService duplicateFileService,
        IDuplicateFileCleanupService duplicateFileCleanupService,
        IFeatureAccessGuard featureAccessGuard)
    {
        _duplicateFileService = duplicateFileService;
        _duplicateFileCleanupService = duplicateFileCleanupService;
        ArgumentNullException.ThrowIfNull(featureAccessGuard);

        GroupedResults = CollectionViewSource.GetDefaultView(Results);
        GroupedResults.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(DuplicateFileCandidateViewModel.GroupKey)));

        BrowseCommand = new RelayCommand(
            Browse,
            featureAccessGuard,
            ApplicationFeature.DuplicateFileFinder,
            FeatureAccessRequirement.Execute,
            () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(
            ScanAsync,
            featureAccessGuard,
            ApplicationFeature.DuplicateFileFinder,
            FeatureAccessRequirement.Execute,
            () => !IsBusy);
        RecycleSelectedCommand = new AsyncRelayCommand(
            RecycleSelectedAsync,
            featureAccessGuard,
            ApplicationFeature.DuplicateFileFinder,
            FeatureAccessRequirement.Execute,
            () => !IsBusy && Results.Any(result => result.IsSelected));
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public ObservableCollection<DuplicateFileCandidateViewModel> Results { get; } = [];
    public ICollectionView GroupedResults { get; }
    public RelayCommand BrowseCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand RecycleSelectedCommand { get; }
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
            RecycleSelectedCommand.RaiseCanExecuteChanged();
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

    public OperationResultPresentation OperationResult
    {
        get => _operationResult;
        private set => SetField(ref _operationResult, value);
    }

    public string GroupsFound => _confirmedGroups.Count.ToString("N0");
    public string DuplicateFiles => Results.Count.ToString("N0");
    public string PotentialReclaimableSpace => MainWindowViewModel.FormatBytes(
        _confirmedGroups.Aggregate(
            0L,
            static (total, group) => SaturatingAdd(total, group.ReclaimableBytes)));
    public string SelectedFiles => Results.Count(result => result.IsSelected).ToString("N0");
    public string SelectedSpace => MainWindowViewModel.FormatBytes(
        Results
            .Where(result => result.IsSelected)
            .Aggregate(
                0L,
                static (total, result) => SaturatingAdd(total, result.Model.SizeBytes)));
    public string SelectionSummary => $"{SelectedFiles} selected - {SelectedSpace}";

    public bool? AreAllRemovableCopiesSelected
    {
        get => BulkSelection.GetAllButOnePerGroupState(
            Results,
            item => item.GroupKey,
            item => item.IsSelected,
            StringComparer.Ordinal);
        set
        {
            var targetSelection = BulkSelection.ResolveTarget(
                value,
                AreAllRemovableCopiesSelected);
            if (targetSelection is null)
            {
                return;
            }

            BulkSelection.SetAllButOnePerGroup(
                Results,
                targetSelection.Value,
                item => item.GroupKey,
                static (item, isSelected) => item.IsSelected = isSelected,
                StringComparer.Ordinal);
            RefreshSelectionSummary();
        }
    }

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

        BeginOperation(
            "Scanning by file size, then verifying matching sizes with SHA-256. No files will be changed.",
            "Starting scan...",
            "Scanning...");
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

            _confirmedGroups = result.Groups;
            PopulateResults(_confirmedGroups);
            Progress = 100;

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
                    ? $"Scan complete. Found {result.Groups.Count:N0} duplicate group(s). Select copies manually; at least one confirmed copy must remain in every group."
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

    private async Task RecycleSelectedAsync()
    {
        var selected = Results.Where(result => result.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            Status = "Select at least one duplicate copy.";
            return;
        }

        var selectedBytes = selected.Aggregate(
            0L,
            static (total, result) => SaturatingAdd(total, result.Model.SizeBytes));
        var answer = MessageBox.Show(
            $"Move {selected.Length:N0} selected duplicate file(s) ({MainWindowViewModel.FormatBytes(selectedBytes)}) to the Windows Recycle Bin?\n\nAt least one unchanged, content-confirmed copy will be retained in every group. Locked, missing, changed, unsafe, or inaccessible files will be skipped. Recycled files can normally be restored from Windows Recycle Bin.",
            "Confirm duplicate cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            Status = "Duplicate cleanup not started.";
            return;
        }

        BeginOperation(
            "Revalidating selected duplicates and moving safe copies to the Windows Recycle Bin...",
            $"0 of {selected.Length:N0} processed",
            "Cleaning...");

        try
        {
            var progress = new Progress<DuplicateFileCleanupProgress>(value =>
            {
                Progress = value.TotalCount == 0
                    ? 100
                    : (int)Math.Round(value.ProcessedCount * 100d / value.TotalCount);
                ProgressText = $"{value.ProcessedCount:N0} of {value.TotalCount:N0} processed";
            });

            var result = await _duplicateFileCleanupService.CleanAsync(
                SelectedFolder,
                _confirmedGroups,
                selected.Select(item => item.Model).ToArray(),
                progress,
                _cancellationTokenSource!.Token);

            ApplyCleanupResult(result.RecycledPaths);
            if (!result.WasCancelled)
            {
                Progress = 100;
            }

            ProgressText = $"{result.RecycledCount + result.SkippedCount:N0} of {selected.Length:N0} processed";
            var elapsed = FormatElapsed(result.Elapsed);

            OperationResult = new OperationResultPresentation(
                true,
                "RECYCLED",
                result.RecycledCount.ToString("N0"),
                result.SkippedCount.ToString("N0"),
                "0",
                MainWindowViewModel.FormatBytes(result.ReclaimedBytes),
                elapsed,
                result.Errors.Count > 0 ? result.Errors[0] : string.Empty);

            if (result.WasCancelled)
            {
                ScanStatus = $"Cleanup cancelled - {elapsed}";
                Status = "Duplicate cleanup was cancelled. Completed Recycle Bin moves remain applied; remaining files were untouched.";
            }
            else if (result.CompletedWithoutErrors)
            {
                ScanStatus = $"Cleanup completed - {elapsed}";
                Status = "Duplicate cleanup completed successfully. Results were refreshed.";
            }
            else
            {
                ScanStatus = $"Cleanup completed - {elapsed} - {result.SkippedCount:N0} skipped";
                Status = "Duplicate cleanup completed with skipped items. Results were refreshed.";
            }
        }
        finally
        {
            EndOperation();
        }
    }

    private void OnCandidatePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(DuplicateFileCandidateViewModel.IsSelected) ||
            sender is not DuplicateFileCandidateViewModel changedItem)
        {
            return;
        }

        if (!_isCorrectingSelection &&
            changedItem.IsSelected &&
            Results
                .Where(item => string.Equals(item.GroupKey, changedItem.GroupKey, StringComparison.Ordinal))
                .All(item => item.IsSelected))
        {
            _isCorrectingSelection = true;
            changedItem.IsSelected = false;
            _isCorrectingSelection = false;
            Status = $"{changedItem.GroupDisplay}: at least one confirmed copy must remain. The final copy was not selected.";
        }

        RefreshSelectionSummary();
    }

    private void ApplyCleanupResult(IReadOnlyCollection<string> recycledPaths)
    {
        var recycledPathSet = recycledPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _confirmedGroups = _confirmedGroups
            .Select(group =>
            {
                var remainingFiles = group.Files
                    .Where(candidate => !recycledPathSet.Contains(candidate.FullPath))
                    .ToArray();

                return remainingFiles.Length > 1
                    ? new DuplicateFileGroup(
                        group.Sha256Hash,
                        group.SizeBytes,
                        remainingFiles)
                    : null;
            })
            .Where(group => group is not null)
            .Cast<DuplicateFileGroup>()
            .ToArray();

        PopulateResults(_confirmedGroups);
    }

    private void PopulateResults(IReadOnlyCollection<DuplicateFileGroup> groups)
    {
        DetachResultHandlers();
        Results.Clear();

        var groupNumber = 1;
        foreach (var group in groups)
        {
            foreach (var candidate in group.Files)
            {
                var viewModel = new DuplicateFileCandidateViewModel(
                    candidate,
                    groupNumber,
                    group.Files.Count,
                    group.ReclaimableBytes);
                viewModel.PropertyChanged += OnCandidatePropertyChanged;
                Results.Add(viewModel);
            }

            groupNumber++;
        }

        RefreshSummary();
        RefreshSelectionSummary();
    }

    private void ClearResults()
    {
        DetachResultHandlers();
        Results.Clear();
        _confirmedGroups = [];
        RefreshSummary();
        RefreshSelectionSummary();
    }

    private void DetachResultHandlers()
    {
        foreach (var result in Results)
        {
            result.PropertyChanged -= OnCandidatePropertyChanged;
        }
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
        OnPropertyChanged(nameof(GroupsFound));
        OnPropertyChanged(nameof(DuplicateFiles));
        OnPropertyChanged(nameof(PotentialReclaimableSpace));
    }

    private void RefreshSelectionSummary()
    {
        OnPropertyChanged(nameof(SelectedFiles));
        OnPropertyChanged(nameof(SelectedSpace));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(AreAllRemovableCopiesSelected));
        RecycleSelectedCommand.RaiseCanExecuteChanged();
    }

    private static long SaturatingAdd(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;

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

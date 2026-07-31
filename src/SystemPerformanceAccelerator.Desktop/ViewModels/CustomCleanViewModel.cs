using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class CustomCleanViewModel : INotifyPropertyChanged
{
    private readonly ICustomCleanService _customCleanService;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _includeTemporaryFiles = true;
    private bool _isBusy;
    private int _progress;
    private string _status =
        "Choose existing Cleaner categories, preview the targets, then confirm cleanup.";
    private string _previewStatus = "Not previewed";
    private OperationResultPresentation _operationResult = OperationResultPresentation.Hidden;

    public CustomCleanViewModel(
        ICustomCleanService customCleanService,
        IFeatureAccessGuard featureAccessGuard)
    {
        _customCleanService = customCleanService ??
            throw new ArgumentNullException(nameof(customCleanService));
        ArgumentNullException.ThrowIfNull(featureAccessGuard);

        PreviewCommand = new AsyncRelayCommand(
            PreviewAsync,
            featureAccessGuard,
            ApplicationFeature.CustomClean,
            FeatureAccessRequirement.Execute,
            () => !IsBusy && SelectedCategoryCount > 0);
        CleanCommand = new AsyncRelayCommand(
            CleanAsync,
            featureAccessGuard,
            ApplicationFeature.CustomClean,
            FeatureAccessRequirement.Execute,
            () => !IsBusy &&
                SelectedCategoryCount > 0 &&
                Results.Any(item => item.IsSelected));
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public ObservableCollection<CustomCleanPreviewItemViewModel> Results { get; } = [];
    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public RelayCommand CancelCommand { get; }

    public bool IncludeTemporaryFiles
    {
        get => _includeTemporaryFiles;
        set
        {
            if (!SetField(ref _includeTemporaryFiles, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedCategoryCount));
            OnPropertyChanged(nameof(SelectedCategoriesText));

            ClearResults();
            Progress = 0;
            PreviewStatus = "Selection changed";
            Status = "Category selection changed. Run Preview before cleaning.";
            OperationResult = OperationResultPresentation.Hidden;
            RefreshSummary();
            RaiseOperationCanExecuteChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
            {
                return;
            }

            RaiseOperationCanExecuteChanged();
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

    public string PreviewStatus
    {
        get => _previewStatus;
        private set => SetField(ref _previewStatus, value);
    }

    public OperationResultPresentation OperationResult
    {
        get => _operationResult;
        private set => SetField(ref _operationResult, value);
    }

    public int SelectedCategoryCount => IncludeTemporaryFiles ? 1 : 0;
    public string SelectedCategoriesText => $"{SelectedCategoryCount:N0} of 1";
    public string FilesFound => Results.Count.ToString("N0");
    public string ReclaimableSpace =>
        MainWindowViewModel.FormatBytes(Results.Sum(item => item.Model.SizeBytes));

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
            CleanCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task PreviewAsync()
    {
        var selectedCategories = GetSelectedCategories();
        if (selectedCategories.Count == 0)
        {
            Status = "Select at least one Cleaner category.";
            return;
        }

        BeginOperation(
            "Previewing...",
            "Scanning selected Cleaner categories in read-only mode...");
        ClearResults();
        RefreshSummary();
        RaiseOperationCanExecuteChanged();

        try
        {
            var result = await _customCleanService.PreviewAsync(
                selectedCategories,
                new Progress<int>(value => Progress = Math.Clamp(value, 0, 100)),
                _cancellationTokenSource!.Token);

            foreach (var item in result.Items)
            {
                var viewModel = new CustomCleanPreviewItemViewModel(item);
                viewModel.PropertyChanged += OnResultPropertyChanged;
                Results.Add(viewModel);
            }

            Progress = 100;
            RefreshSummary();
            RaiseOperationCanExecuteChanged();

            var elapsed = FormatElapsed(result.Elapsed);
            PreviewStatus = result.Errors.Count == 0
                ? $"Preview complete - {elapsed}"
                : $"Preview complete - {elapsed} - {result.Errors.Count:N0} issue(s)";
            Status = result.Errors.Count == 0
                ? $"Preview complete. Found {result.Items.Count:N0} file(s). Review the list before cleaning."
                : $"Preview complete with {result.Errors.Count:N0} issue(s). First issue: {result.Errors[0]}";
        }
        catch (OperationCanceledException)
        {
            PreviewStatus = "Preview cancelled";
            Status = "Custom Clean preview cancelled. No files were deleted or changed.";
        }
        catch (Exception ex)
        {
            PreviewStatus = "Preview failed";
            Status = $"Custom Clean preview failed safely. No files were changed. {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task CleanAsync()
    {
        var selectedCategories = GetSelectedCategories();
        var previewItems = Results
            .Where(item => item.IsSelected)
            .Select(item => item.Model)
            .ToArray();

        if (selectedCategories.Count == 0 || previewItems.Length == 0)
        {
            Status = "Run Preview and select at least one file before cleaning.";
            return;
        }

        var totalBytes = previewItems.Sum(item => item.SizeBytes);
        var answer = MessageBox.Show(
            $"Delete {previewItems.Length:N0} selected previewed temporary file(s) from the selected Custom Clean categories and attempt to reclaim {MainWindowViewModel.FormatBytes(totalBytes)}?\n\nThis cannot be undone.",
            "Confirm Custom Clean",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            Status = "Custom Clean cleanup not started.";
            return;
        }

        BeginOperation(
            "Cleaning...",
            "Cleaning the selected previewed files from the selected categories...");

        try
        {
            var result = await _customCleanService.CleanAsync(
                selectedCategories,
                previewItems,
                new Progress<int>(value => Progress = Math.Clamp(value, 0, 100)),
                _cancellationTokenSource!.Token);

            RemoveDeletedResults();
            Progress = 100;
            RefreshSummary();
            RaiseOperationCanExecuteChanged();

            var elapsed = FormatElapsed(result.Elapsed);
            PreviewStatus = result.CompletedWithoutIssues
                ? $"Cleanup complete - {elapsed}"
                : $"Cleanup complete - {elapsed} - issues reported";

            OperationResult = new OperationResultPresentation(
                true,
                "DELETED",
                result.DeletedCount.ToString("N0"),
                result.SkippedCount.ToString("N0"),
                result.FailedCount.ToString("N0"),
                MainWindowViewModel.FormatBytes(result.ReclaimedBytes),
                elapsed,
                result.Errors.Count > 0 ? result.Errors[0] : string.Empty);
            Status = result.CompletedWithoutIssues
                ? "Custom Clean completed successfully."
                : "Custom Clean completed with skipped or failed items.";
        }
        catch (OperationCanceledException)
        {
            RemoveDeletedResults();
            RefreshSummary();
            RaiseOperationCanExecuteChanged();
            PreviewStatus = "Cleanup cancelled";
            Status = "Custom Clean cancelled. Files already deleted remain deleted; remaining files were untouched.";
        }
        catch (Exception ex)
        {
            RemoveDeletedResults();
            RefreshSummary();
            RaiseOperationCanExecuteChanged();
            PreviewStatus = "Cleanup failed";
            Status =
                $"Custom Clean stopped unexpectedly. Files already deleted remain deleted; remaining files were not intentionally changed. {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    private void OnResultPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CustomCleanPreviewItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(AreAllResultsSelected));
            CleanCommand.RaiseCanExecuteChanged();
        }
    }

    private void ClearResults()
    {
        foreach (var result in Results)
        {
            result.PropertyChanged -= OnResultPropertyChanged;
        }

        Results.Clear();
        OnPropertyChanged(nameof(AreAllResultsSelected));
        CleanCommand.RaiseCanExecuteChanged();
    }

    private IReadOnlyCollection<CustomCleanCategory> GetSelectedCategories()
    {
        var categories = new List<CustomCleanCategory>();
        if (IncludeTemporaryFiles)
        {
            categories.Add(CustomCleanCategory.TemporaryFiles);
        }

        return categories;
    }

    private void BeginOperation(string operationStatus, string status)
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        Progress = 0;
        OperationResult = OperationResultPresentation.Hidden;
        PreviewStatus = operationStatus;
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

    private void RemoveDeletedResults()
    {
        foreach (var item in Results
            .Where(item => !File.Exists(item.FullPath))
            .ToArray())
        {
            item.PropertyChanged -= OnResultPropertyChanged;
            Results.Remove(item);
        }
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(FilesFound));
        OnPropertyChanged(nameof(ReclaimableSpace));
        OnPropertyChanged(nameof(AreAllResultsSelected));
    }

    private void RaiseOperationCanExecuteChanged()
    {
        PreviewCommand.RaiseCanExecuteChanged();
        CleanCommand.RaiseCanExecuteChanged();
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

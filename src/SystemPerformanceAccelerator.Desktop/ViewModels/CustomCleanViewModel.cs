using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
        "Choose existing Cleaner categories, then preview the files. Nothing can be deleted here.";
    private string _previewStatus = "Not previewed";

    public CustomCleanViewModel(ICustomCleanService customCleanService)
    {
        _customCleanService = customCleanService ??
            throw new ArgumentNullException(nameof(customCleanService));
        PreviewCommand = new AsyncRelayCommand(
            PreviewAsync,
            () => !IsBusy && SelectedCategoryCount > 0);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public ObservableCollection<CustomCleanPreviewItemViewModel> Results { get; } = [];
    public AsyncRelayCommand PreviewCommand { get; }
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

            Results.Clear();
            Progress = 0;
            PreviewStatus = "Selection changed";
            Status = "Category selection changed. Run Preview to refresh the read-only results.";
            RefreshSummary();
            PreviewCommand.RaiseCanExecuteChanged();
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

            PreviewCommand.RaiseCanExecuteChanged();
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

    public int SelectedCategoryCount => IncludeTemporaryFiles ? 1 : 0;
    public string SelectedCategoriesText => $"{SelectedCategoryCount:N0} of 1";
    public string FilesFound => Results.Count.ToString("N0");
    public string ReclaimableSpace =>
        MainWindowViewModel.FormatBytes(Results.Sum(item => item.Model.SizeBytes));

    private async Task PreviewAsync()
    {
        var selectedCategories = GetSelectedCategories();
        if (selectedCategories.Count == 0)
        {
            Status = "Select at least one Cleaner category.";
            return;
        }

        BeginOperation();
        Results.Clear();
        RefreshSummary();

        try
        {
            var result = await _customCleanService.PreviewAsync(
                selectedCategories,
                new Progress<int>(value => Progress = Math.Clamp(value, 0, 100)),
                _cancellationTokenSource!.Token);

            foreach (var item in result.Items)
            {
                Results.Add(new CustomCleanPreviewItemViewModel(item));
            }

            Progress = 100;
            RefreshSummary();

            var elapsed = FormatElapsed(result.Elapsed);
            PreviewStatus = result.Errors.Count == 0
                ? $"Completed - {elapsed}"
                : $"Completed - {elapsed} - {result.Errors.Count:N0} issue(s)";
            Status = result.Errors.Count == 0
                ? $"Read-only preview complete. Found {result.Items.Count:N0} file(s)."
                : $"Read-only preview complete with {result.Errors.Count:N0} issue(s). First issue: {result.Errors[0]}";
        }
        catch (OperationCanceledException)
        {
            PreviewStatus = "Cancelled";
            Status = "Custom Clean preview cancelled. No files were deleted or changed.";
        }
        catch (Exception ex)
        {
            PreviewStatus = "Failed";
            Status = $"Custom Clean preview failed safely. No files were changed. {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
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

    private void BeginOperation()
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        Progress = 0;
        PreviewStatus = "Previewing...";
        Status = "Scanning selected Cleaner categories in read-only mode...";
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
        OnPropertyChanged(nameof(ReclaimableSpace));
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

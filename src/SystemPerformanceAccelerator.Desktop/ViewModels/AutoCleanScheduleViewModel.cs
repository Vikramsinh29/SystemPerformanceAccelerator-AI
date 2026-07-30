using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;
using SystemPerformanceAccelerator.Desktop.Commands;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class AutoCleanScheduleViewModel : INotifyPropertyChanged
{
    private static readonly string[] SupportedTimeFormats =
    [
        "H:mm",
        "HH:mm"
    ];

    private readonly IAutoCleanScheduleService _scheduleService;
    private readonly ICustomCleanService _customCleanService;
    private readonly Func<DateTime> _nowProvider;
    private CancellationTokenSource? _cancellationTokenSource;
    private AutoCleanScheduleItemViewModel? _selectedSchedule;
    private Guid? _editingScheduleId;
    private bool _isEditorOpen;
    private string _scheduleName = "Auto Clean Schedule";
    private AutoCleanScheduleFrequency _frequency = AutoCleanScheduleFrequency.Daily;
    private string _runAtLocalTimeText = "09:00";
    private DayOfWeek _weeklyDay = DayOfWeek.Monday;
    private string _monthlyDayText = "1";
    private bool _includeTemporaryFiles = true;
    private bool _isScheduleEnabled;
    private bool _isBusy;
    private int _previewProgress;
    private string _status =
        "Create a local schedule plan. Nothing will run or delete files automatically.";
    private string _previewStatus = "Not previewed";
    private string _previewFilesFound = "0";
    private string _previewReclaimableSpace = "0 B";
    private string _previewIssues = "0";

    public AutoCleanScheduleViewModel(
        IAutoCleanScheduleService scheduleService,
        ICustomCleanService customCleanService,
        IFeatureAccessGuard featureAccessGuard,
        Func<DateTime>? nowProvider = null)
    {
        _scheduleService = scheduleService ??
            throw new ArgumentNullException(nameof(scheduleService));
        _customCleanService = customCleanService ??
            throw new ArgumentNullException(nameof(customCleanService));
        ArgumentNullException.ThrowIfNull(featureAccessGuard);
        _nowProvider = nowProvider ?? (() => DateTime.Now);

        Frequencies = Enum.GetValues<AutoCleanScheduleFrequency>();
        WeekDays = Enum.GetValues<DayOfWeek>();

        NewScheduleCommand = new RelayCommand(
            StartNewSchedule,
            featureAccessGuard,
            ApplicationFeature.AutoCleanSchedule,
            FeatureAccessRequirement.Execute,
            () => !IsBusy && !IsEditorOpen);
        EditSelectedScheduleCommand = new RelayCommand(
            EditSelectedSchedule,
            featureAccessGuard,
            ApplicationFeature.AutoCleanSchedule,
            FeatureAccessRequirement.Execute,
            () => !IsBusy && !IsEditorOpen && SelectedSchedule is not null);
        BackToSchedulesCommand = new RelayCommand(
            BackToSchedules,
            () => !IsBusy && IsEditorOpen);
        SaveScheduleCommand = new RelayCommand(
            SaveSchedule,
            featureAccessGuard,
            ApplicationFeature.AutoCleanSchedule,
            FeatureAccessRequirement.Execute,
            () => !IsBusy && IsEditorOpen);
        RemoveScheduleCommand = new RelayCommand(
            RemoveSchedule,
            featureAccessGuard,
            ApplicationFeature.AutoCleanSchedule,
            FeatureAccessRequirement.Execute,
            () => !IsBusy && !IsEditorOpen && SelectedSchedule is not null);
        PreviewNowCommand = new AsyncRelayCommand(
            PreviewNowAsync,
            featureAccessGuard,
            ApplicationFeature.AutoCleanSchedule,
            FeatureAccessRequirement.Execute,
            () => !IsBusy && IsEditorOpen && IncludeTemporaryFiles);
        CancelPreviewCommand = new RelayCommand(
            CancelPreview,
            () => IsBusy);

        LoadSchedules();
    }

    public ObservableCollection<AutoCleanScheduleItemViewModel> Schedules { get; } = [];
    public IReadOnlyList<AutoCleanScheduleFrequency> Frequencies { get; }
    public IReadOnlyList<DayOfWeek> WeekDays { get; }
    public RelayCommand NewScheduleCommand { get; }
    public RelayCommand EditSelectedScheduleCommand { get; }
    public RelayCommand BackToSchedulesCommand { get; }
    public RelayCommand SaveScheduleCommand { get; }
    public RelayCommand RemoveScheduleCommand { get; }
    public AsyncRelayCommand PreviewNowCommand { get; }
    public RelayCommand CancelPreviewCommand { get; }

    public string SchedulesPath => _scheduleService.SchedulesPath;
    public string ScheduleCountText => Schedules.Count.ToString("N0");
    public string EnabledScheduleCountText =>
        Schedules.Count(item => item.IsEnabled).ToString("N0");
    public bool HasSchedules => Schedules.Count > 0;
    public bool IsEmptyStateVisible => !HasSchedules;
    public bool IsOverviewVisible => !IsEditorOpen;
    public bool IsEditorVisible => IsEditorOpen;
    public string NextPlannedRunText
    {
        get
        {
            var now = _nowProvider();
            var nextRun = Schedules
                .Select(item => _scheduleService.CalculateNextRun(
                    item.Model,
                    now))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .DefaultIfEmpty()
                .Min();

            return nextRun == default
                ? "No enabled schedules"
                : nextRun.ToString("dd MMM yyyy, hh:mm tt");
        }
    }

    public AutoCleanScheduleItemViewModel? SelectedSchedule
    {
        get => _selectedSchedule;
        set
        {
            if (!SetField(ref _selectedSchedule, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedScheduleSummary));
            RaiseCommandStates();
        }
    }

    public string SelectedScheduleSummary => SelectedSchedule is null
        ? "Select a schedule card to edit or remove it."
        : $"Selected: {SelectedSchedule.Name}";

    public string ScheduleName
    {
        get => _scheduleName;
        set
        {
            if (SetField(ref _scheduleName, value))
            {
                OnEditorChanged();
            }
        }
    }

    public AutoCleanScheduleFrequency Frequency
    {
        get => _frequency;
        set
        {
            if (!SetField(ref _frequency, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsWeeklyFrequency));
            OnPropertyChanged(nameof(IsMonthlyFrequency));
            OnEditorChanged();
        }
    }

    public string RunAtLocalTimeText
    {
        get => _runAtLocalTimeText;
        set
        {
            if (SetField(ref _runAtLocalTimeText, value))
            {
                OnEditorChanged();
            }
        }
    }

    public DayOfWeek WeeklyDay
    {
        get => _weeklyDay;
        set
        {
            if (SetField(ref _weeklyDay, value))
            {
                OnEditorChanged();
            }
        }
    }

    public string MonthlyDayText
    {
        get => _monthlyDayText;
        set
        {
            if (SetField(ref _monthlyDayText, value))
            {
                OnEditorChanged();
            }
        }
    }

    public bool IncludeTemporaryFiles
    {
        get => _includeTemporaryFiles;
        set
        {
            if (!SetField(ref _includeTemporaryFiles, value))
            {
                return;
            }

            OnEditorChanged();
            PreviewNowCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsScheduleEnabled
    {
        get => _isScheduleEnabled;
        set
        {
            if (SetField(ref _isScheduleEnabled, value))
            {
                OnEditorChanged();
            }
        }
    }

    public bool IsWeeklyFrequency =>
        Frequency == AutoCleanScheduleFrequency.Weekly;
    public bool IsMonthlyFrequency =>
        Frequency == AutoCleanScheduleFrequency.Monthly;
    public bool IsEditingExistingSchedule => _editingScheduleId.HasValue;
    public string EditorTitle => IsEditingExistingSchedule
        ? "Edit schedule"
        : "Create a schedule";
    public string EditorSubtitle => IsEditingExistingSchedule
        ? "Update this local plan without affecting your other saved schedules."
        : "Create a separate local plan. Existing schedules will remain unchanged.";
    public string SaveButtonText => IsEditingExistingSchedule
        ? "Save changes"
        : "Create schedule";
    public string EditorNextRunText
    {
        get
        {
            if (!TryBuildEditorSchedule(out var schedule, out _))
            {
                return "Complete the schedule details";
            }

            var nextRun = _scheduleService.CalculateNextRun(
                schedule,
                _nowProvider());
            return nextRun.HasValue
                ? nextRun.Value.ToString("dd MMM yyyy, hh:mm tt")
                : "Disabled";
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

            RaiseCommandStates();
        }
    }

    public int PreviewProgress
    {
        get => _previewProgress;
        private set => SetField(ref _previewProgress, value);
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

    public string PreviewFilesFound
    {
        get => _previewFilesFound;
        private set => SetField(ref _previewFilesFound, value);
    }

    public string PreviewReclaimableSpace
    {
        get => _previewReclaimableSpace;
        private set => SetField(ref _previewReclaimableSpace, value);
    }

    public string PreviewIssues
    {
        get => _previewIssues;
        private set => SetField(ref _previewIssues, value);
    }

    private bool IsEditorOpen
    {
        get => _isEditorOpen;
        set
        {
            if (!SetField(ref _isEditorOpen, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsOverviewVisible));
            OnPropertyChanged(nameof(IsEditorVisible));
            RaiseCommandStates();
        }
    }

    private void LoadSchedules()
    {
        var result = _scheduleService.Load();
        var now = _nowProvider();

        foreach (var schedule in result.Schedules)
        {
            Schedules.Add(new AutoCleanScheduleItemViewModel(
                schedule,
                _scheduleService.CalculateNextRun(schedule, now)));
        }

        SelectedSchedule = Schedules.FirstOrDefault();
        ResetEditor();
        IsEditorOpen = false;

        Status = result.HasWarning
            ? result.Warning
            : Schedules.Count == 0
                ? "No schedules saved. Create your first local schedule plan."
                : $"Loaded {Schedules.Count:N0} local schedule(s).";
        RefreshScheduleSummary();
    }

    private void StartNewSchedule()
    {
        _editingScheduleId = null;
        ResetEditor();
        IsEditorOpen = true;
        Status = "Creating a new schedule. Existing schedules will not be changed.";
    }

    private void EditSelectedSchedule()
    {
        if (SelectedSchedule is null)
        {
            Status = "Select a schedule before editing it.";
            return;
        }

        LoadEditor(SelectedSchedule.Model);
        IsEditorOpen = true;
        Status = $"Editing '{SelectedSchedule.Name}'. Other schedules will remain unchanged.";
    }

    private void BackToSchedules()
    {
        if (IsBusy)
        {
            return;
        }

        IsEditorOpen = false;
        _editingScheduleId = null;
        ResetPreview("Not previewed");
        Status = Schedules.Count == 0
            ? "No schedules saved. Create your first local schedule plan."
            : "Schedule overview ready.";
    }

    private void SaveSchedule()
    {
        if (!TryBuildEditorSchedule(out var schedule, out var error))
        {
            Status = error;
            return;
        }

        AutoCleanScheduleItemViewModel? existing = null;
        AutoCleanSchedule[] proposedSchedules;
        if (_editingScheduleId.HasValue)
        {
            existing = Schedules.FirstOrDefault(
                item => item.Id == _editingScheduleId.Value);
            if (existing is null)
            {
                Status = "The selected schedule no longer exists. Return to the overview and create a new schedule.";
                return;
            }

            proposedSchedules = Schedules
                .Select(item => item == existing ? schedule : item.Model)
                .ToArray();
        }
        else
        {
            proposedSchedules = Schedules
                .Select(item => item.Model)
                .Append(schedule)
                .ToArray();
        }

        try
        {
            _scheduleService.Save(proposedSchedules);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            InvalidOperationException or
            NotSupportedException)
        {
            Status = $"The schedule could not be saved locally. {ex.Message}";
            return;
        }

        var now = _nowProvider();
        AutoCleanScheduleItemViewModel savedItem;
        if (existing is not null)
        {
            existing.Update(
                schedule,
                _scheduleService.CalculateNextRun(schedule, now));
            savedItem = existing;
        }
        else
        {
            savedItem = new AutoCleanScheduleItemViewModel(
                schedule,
                _scheduleService.CalculateNextRun(schedule, now));
            Schedules.Add(savedItem);
        }

        SelectedSchedule = savedItem;
        RefreshAllNextRuns();
        IsEditorOpen = false;
        _editingScheduleId = null;
        Status = existing is null
            ? $"Created '{schedule.Name}'. {Schedules.Count:N0} schedule(s) are now saved locally."
            : $"Saved changes to '{schedule.Name}'. Other schedules were not changed.";

        RefreshScheduleSummary();
        RaiseCommandStates();
    }

    private void RemoveSchedule()
    {
        if (SelectedSchedule is null)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"Remove the local schedule '{SelectedSchedule.Name}'?\n\nThis removes only the schedule record. No files will be changed.",
            "Remove Auto Clean schedule",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            Status = "Schedule removal cancelled.";
            return;
        }

        var scheduleToRemove = SelectedSchedule;
        var removedName = scheduleToRemove.Name;
        var removedIndex = Schedules.IndexOf(scheduleToRemove);
        var proposedSchedules = Schedules
            .Where(item => item != scheduleToRemove)
            .Select(item => item.Model)
            .ToArray();

        try
        {
            _scheduleService.Save(proposedSchedules);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            InvalidOperationException or
            NotSupportedException)
        {
            Status = $"The updated schedule list could not be saved locally. {ex.Message}";
            return;
        }

        Schedules.Remove(scheduleToRemove);
        SelectedSchedule = Schedules.Count > 0
            ? Schedules[Math.Min(removedIndex, Schedules.Count - 1)]
            : null;

        Status = $"Removed local schedule '{removedName}'. No files were changed.";
        RefreshScheduleSummary();
        RaiseCommandStates();
    }

    private async Task PreviewNowAsync()
    {
        if (!TryBuildEditorSchedule(out var schedule, out var error))
        {
            Status = error;
            return;
        }

        BeginPreview();
        try
        {
            var result = await _customCleanService.PreviewAsync(
                schedule.Categories,
                new Progress<int>(value =>
                    PreviewProgress = Math.Clamp(value, 0, 100)),
                _cancellationTokenSource!.Token);

            PreviewProgress = 100;
            PreviewFilesFound = result.Items.Count.ToString("N0");
            PreviewReclaimableSpace = MainWindowViewModel.FormatBytes(
                result.Items.Sum(item => item.SizeBytes));
            PreviewIssues = result.Errors.Count.ToString("N0");
            PreviewStatus = result.Errors.Count == 0
                ? "Preview complete"
                : "Preview completed with issues";
            Status = result.Errors.Count == 0
                ? "Manual preview completed. No files were deleted or changed."
                : $"Manual preview completed safely with {result.Errors.Count:N0} issue(s). First issue: {result.Errors[0]}";
        }
        catch (OperationCanceledException)
        {
            PreviewStatus = "Preview cancelled";
            Status = "Manual preview cancelled. No files were deleted or changed.";
        }
        catch (Exception ex)
        {
            PreviewStatus = "Preview failed";
            Status = $"Manual preview failed safely. No files were changed. {ex.Message}";
        }
        finally
        {
            EndPreview();
        }
    }

    private bool TryBuildEditorSchedule(
        out AutoCleanSchedule schedule,
        out string error)
    {
        schedule = null!;
        error = string.Empty;

        var name = ScheduleName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Enter a schedule name.";
            return false;
        }

        if (name.Length > AutoCleanSchedule.MaximumNameLength)
        {
            error = $"Schedule names can contain up to {AutoCleanSchedule.MaximumNameLength} characters.";
            return false;
        }

        if (!Enum.IsDefined(Frequency))
        {
            error = "Choose a supported schedule frequency.";
            return false;
        }

        if (!TimeOnly.TryParseExact(
                RunAtLocalTimeText.Trim(),
                SupportedTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var runAtLocalTime))
        {
            error = "Enter the local run time as HH:mm, for example 09:30.";
            return false;
        }

        if (Frequency == AutoCleanScheduleFrequency.Weekly &&
            !Enum.IsDefined(WeeklyDay))
        {
            error = "Choose a valid weekly day.";
            return false;
        }

        var monthlyDay = 1;
        if (Frequency == AutoCleanScheduleFrequency.Monthly &&
            (!int.TryParse(
                MonthlyDayText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out monthlyDay) ||
             monthlyDay is < 1 or > 31))
        {
            error = "Enter a monthly day from 1 to 31.";
            return false;
        }

        var categories = IncludeTemporaryFiles
            ? new[] { CustomCleanCategory.TemporaryFiles }
            : Array.Empty<CustomCleanCategory>();
        if (categories.Length == 0)
        {
            error = "Select at least one existing Cleaner category.";
            return false;
        }

        schedule = new AutoCleanSchedule(
            _editingScheduleId ?? Guid.NewGuid(),
            name,
            IsScheduleEnabled,
            Frequency,
            runAtLocalTime,
            WeeklyDay,
            monthlyDay,
            categories);
        return true;
    }

    private void LoadEditor(AutoCleanSchedule schedule)
    {
        _editingScheduleId = schedule.Id;
        SetEditorFields(
            schedule.Name,
            schedule.Frequency,
            schedule.RunAtLocalTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            schedule.WeeklyDay,
            schedule.MonthlyDay.ToString(CultureInfo.InvariantCulture),
            schedule.Categories.Contains(CustomCleanCategory.TemporaryFiles),
            schedule.IsEnabled);
        ResetPreview("Not previewed");
    }

    private void ResetEditor()
    {
        _editingScheduleId = null;
        SetEditorFields(
            "Auto Clean Schedule",
            AutoCleanScheduleFrequency.Daily,
            "09:00",
            DayOfWeek.Monday,
            "1",
            true,
            false);
        ResetPreview("Not previewed");
    }

    private void SetEditorFields(
        string name,
        AutoCleanScheduleFrequency frequency,
        string timeText,
        DayOfWeek weeklyDay,
        string monthlyDayText,
        bool includeTemporaryFiles,
        bool isEnabled)
    {
        _scheduleName = name;
        _frequency = frequency;
        _runAtLocalTimeText = timeText;
        _weeklyDay = weeklyDay;
        _monthlyDayText = monthlyDayText;
        _includeTemporaryFiles = includeTemporaryFiles;
        _isScheduleEnabled = isEnabled;

        OnPropertyChanged(nameof(ScheduleName));
        OnPropertyChanged(nameof(Frequency));
        OnPropertyChanged(nameof(RunAtLocalTimeText));
        OnPropertyChanged(nameof(WeeklyDay));
        OnPropertyChanged(nameof(MonthlyDayText));
        OnPropertyChanged(nameof(IncludeTemporaryFiles));
        OnPropertyChanged(nameof(IsScheduleEnabled));
        OnPropertyChanged(nameof(IsWeeklyFrequency));
        OnPropertyChanged(nameof(IsMonthlyFrequency));
        OnPropertyChanged(nameof(IsEditingExistingSchedule));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorSubtitle));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(EditorNextRunText));
        RaiseCommandStates();
    }

    private void OnEditorChanged()
    {
        OnPropertyChanged(nameof(EditorNextRunText));
        ResetPreview("Selection changed");
        RaiseCommandStates();
    }

    private void BeginPreview()
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        PreviewProgress = 0;
        PreviewStatus = "Previewing...";
        PreviewFilesFound = "0";
        PreviewReclaimableSpace = "0 B";
        PreviewIssues = "0";
        Status = "Running a read-only preview of the selected Cleaner categories...";
        IsBusy = true;
    }

    private void EndPreview()
    {
        IsBusy = false;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void CancelPreview() => _cancellationTokenSource?.Cancel();

    private void ResetPreview(string previewStatus)
    {
        PreviewProgress = 0;
        PreviewStatus = previewStatus;
        PreviewFilesFound = "0";
        PreviewReclaimableSpace = "0 B";
        PreviewIssues = "0";
    }

    private void RefreshAllNextRuns()
    {
        var now = _nowProvider();
        foreach (var item in Schedules)
        {
            item.Update(
                item.Model,
                _scheduleService.CalculateNextRun(item.Model, now));
        }

        RefreshScheduleSummary();
    }

    private void RefreshScheduleSummary()
    {
        OnPropertyChanged(nameof(ScheduleCountText));
        OnPropertyChanged(nameof(EnabledScheduleCountText));
        OnPropertyChanged(nameof(NextPlannedRunText));
        OnPropertyChanged(nameof(HasSchedules));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
    }

    private void RaiseCommandStates()
    {
        NewScheduleCommand.RaiseCanExecuteChanged();
        EditSelectedScheduleCommand.RaiseCanExecuteChanged();
        BackToSchedulesCommand.RaiseCanExecuteChanged();
        SaveScheduleCommand.RaiseCanExecuteChanged();
        RemoveScheduleCommand.RaiseCanExecuteChanged();
        PreviewNowCommand.RaiseCanExecuteChanged();
        CancelPreviewCommand.RaiseCanExecuteChanged();
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

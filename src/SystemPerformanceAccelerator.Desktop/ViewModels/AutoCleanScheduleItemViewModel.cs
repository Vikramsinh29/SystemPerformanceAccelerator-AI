using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class AutoCleanScheduleItemViewModel : INotifyPropertyChanged
{
    private AutoCleanSchedule _model;
    private DateTime? _nextRun;

    public AutoCleanScheduleItemViewModel(
        AutoCleanSchedule model,
        DateTime? nextRun)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _nextRun = nextRun;
    }

    public AutoCleanSchedule Model => _model;
    public Guid Id => _model.Id;
    public string Name => _model.Name;
    public bool IsEnabled => _model.IsEnabled;
    public string StateText => IsEnabled ? "Enabled" : "Disabled";
    public string RunAtTimeText => _model.RunAtLocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string FrequencyText => _model.Frequency switch
    {
        AutoCleanScheduleFrequency.Daily =>
            $"Every day at {RunAtTimeText}",
        AutoCleanScheduleFrequency.Weekly =>
            $"Every {_model.WeeklyDay} at {RunAtTimeText}",
        AutoCleanScheduleFrequency.Monthly =>
            $"Day {_model.MonthlyDay} of each month at {RunAtTimeText}",
        _ => "Unsupported frequency"
    };
    public string CategoriesText => _model.Categories.Count == 1
        ? "Current-user temporary files"
        : $"{_model.Categories.Count:N0} Cleaner categories";
    public string NextRunText => _nextRun.HasValue
        ? _nextRun.Value.ToString("dd MMM yyyy, hh:mm tt")
        : "Not scheduled";
    public string NextRunCaption => _nextRun.HasValue
        ? "Next planned run"
        : "Schedule state";
    public bool HasLastManualRun => _model.LastManualRun is not null;
    public string LastManualRunText => _model.LastManualRun is { } result
        ? result.CompletedAtLocal.ToString("dd MMM yyyy, hh:mm tt")
        : "Never run manually";
    public string LastManualRunDetail => _model.LastManualRun is { } result
        ? $"{result.DeletedCount:N0} deleted • {MainWindowViewModel.FormatBytes(result.ReclaimedBytes)} reclaimed"
        : "Use Run now for a reviewed, confirmed cleanup.";

    public void Update(
        AutoCleanSchedule model,
        DateTime? nextRun)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _nextRun = nextRun;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(RunAtTimeText));
        OnPropertyChanged(nameof(FrequencyText));
        OnPropertyChanged(nameof(CategoriesText));
        OnPropertyChanged(nameof(NextRunText));
        OnPropertyChanged(nameof(NextRunCaption));
        OnPropertyChanged(nameof(HasLastManualRun));
        OnPropertyChanged(nameof(LastManualRunText));
        OnPropertyChanged(nameof(LastManualRunDetail));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed record WindowsRepairPastRecordViewModel(
    string RecordType,
    string Reference,
    string Outcome,
    DateTimeOffset CompletedUtc,
    TimeSpan Duration,
    string Summary)
{
    public string CompletedText =>
        CompletedUtc.ToLocalTime().ToString("dd MMM yyyy  HH:mm");

    public string DurationText => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours:N0} hr {Duration.Minutes:00} min"
        : $"{(int)Duration.TotalMinutes:N0} min {Duration.Seconds:00} sec";
}

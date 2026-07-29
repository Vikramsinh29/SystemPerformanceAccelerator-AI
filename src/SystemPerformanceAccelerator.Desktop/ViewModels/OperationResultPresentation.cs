namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed record OperationResultPresentation(
    bool IsVisible,
    string ProcessedLabel,
    string ProcessedValue,
    string SkippedValue,
    string FailedValue,
    string ReclaimedValue,
    string DurationValue,
    string Detail)
{
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public static OperationResultPresentation Hidden { get; } = new(
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);
}

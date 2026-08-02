namespace SystemPerformanceAccelerator.Core.Models;

public sealed record ApplicationSettings(
    ApplicationTheme Theme,
    bool ConfirmBeforeCleanup,
    int LargeFileMinimumSizeMb,
    int SystemMonitorRefreshIntervalSeconds)
{
    public const int MinimumLargeFileSizeMb = 1;
    public const int MaximumLargeFileSizeMb = 1_048_576;
    public const int MinimumMonitorRefreshSeconds = 1;
    public const int MaximumMonitorRefreshSeconds = 10;

    public bool LocalDiagnosticsEnabled { get; init; }

    public bool IncludeHardwareSummaryInDiagnosticExport { get; init; }

    public string LastReviewedDiagnosticErrorReference { get; init; } =
        string.Empty;

    public static ApplicationSettings Default { get; } = new(
        ApplicationTheme.System,
        true,
        100,
        1)
    {
        LocalDiagnosticsEnabled = false,
        IncludeHardwareSummaryInDiagnosticExport = false,
        LastReviewedDiagnosticErrorReference = string.Empty
    };
}

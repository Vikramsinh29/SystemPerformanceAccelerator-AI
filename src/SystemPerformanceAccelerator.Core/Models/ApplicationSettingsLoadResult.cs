namespace SystemPerformanceAccelerator.Core.Models;

public sealed record ApplicationSettingsLoadResult(
    ApplicationSettings Settings,
    string Warning)
{
    public bool HasWarning => !string.IsNullOrWhiteSpace(Warning);
}

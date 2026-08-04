namespace SystemPerformanceAccelerator.Infrastructure.Configuration;

public sealed class DesktopApiOptionsProvider
{
    private const string EnvironmentVariableName =
        "PC_SPA_API_ENVIRONMENT";
    private const string BaseUrlVariableName =
        "PC_SPA_API_BASE_URL";
    private const string TimeoutVariableName =
        "PC_SPA_API_TIMEOUT_SECONDS";

    public DesktopApiOptions Load()
    {
        var defaults = DesktopApiOptions.Default;
        var environment = ParseEnvironment(
            Environment.GetEnvironmentVariable(EnvironmentVariableName));
        var overrideBaseUrl = ParseAbsoluteUri(
            Environment.GetEnvironmentVariable(BaseUrlVariableName));
        var timeout = ParseTimeout(
            Environment.GetEnvironmentVariable(TimeoutVariableName))
            ?? defaults.Timeout;

        var activeEnvironment = environment ?? defaults.Environment;
        return defaults with
        {
            Environment = activeEnvironment,
            Timeout = timeout,
            DevelopmentBaseUrl =
                activeEnvironment == DesktopApiEnvironment.Development &&
                overrideBaseUrl is not null
                    ? overrideBaseUrl
                    : defaults.DevelopmentBaseUrl,
            ProductionBaseUrl =
                activeEnvironment == DesktopApiEnvironment.Production &&
                overrideBaseUrl is not null
                    ? overrideBaseUrl
                    : defaults.ProductionBaseUrl
        };
    }

    private static DesktopApiEnvironment? ParseEnvironment(
        string? value) =>
        Enum.TryParse<DesktopApiEnvironment>(
            value,
            ignoreCase: true,
            out var parsed)
            ? parsed
            : null;

    private static Uri? ParseAbsoluteUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : null;

    private static TimeSpan? ParseTimeout(string? value) =>
        int.TryParse(value, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
}

namespace SystemPerformanceAccelerator.Infrastructure.Configuration;

public sealed record DesktopApiOptions(
    Uri DevelopmentBaseUrl,
    Uri ProductionBaseUrl,
    DesktopApiEnvironment Environment,
    TimeSpan Timeout)
{
    public Uri ActiveBaseUrl =>
        Environment == DesktopApiEnvironment.Production
            ? ProductionBaseUrl
            : DevelopmentBaseUrl;

    public static DesktopApiOptions Default { get; } = new(
        new Uri("https://localhost:8787/"),
        new Uri("https://pc-spa-web.pc-spa-feedback.workers.dev/"),
        DesktopApiEnvironment.Production,
        TimeSpan.FromSeconds(15));
}

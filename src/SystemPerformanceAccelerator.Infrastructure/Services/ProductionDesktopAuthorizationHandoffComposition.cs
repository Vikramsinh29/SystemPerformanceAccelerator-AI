using SystemPerformanceAccelerator.Core.Interfaces;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public static class ProductionDesktopAuthorizationHandoffComposition
{
    public static readonly Uri ExchangeUri =
        new(
            "https://pc-spa-licensing-v2-production.pc-spa-feedback.workers.dev/installation-authorization/exchange",
            UriKind.Absolute);

    public static DesktopAuthorizationHandoffService Create(
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        return Create(
            httpClient,
            new WindowsDesktopCredentialStore());
    }

    internal static DesktopAuthorizationHandoffService Create(
        HttpClient httpClient,
        IDesktopCredentialStore credentialStore)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credentialStore);

        var client =
            new DesktopInstallationAuthorizationClient(
                httpClient,
                ExchangeUri);

        return new DesktopAuthorizationHandoffService(
            client.ExchangeAsync,
            credentialStore);
    }
}
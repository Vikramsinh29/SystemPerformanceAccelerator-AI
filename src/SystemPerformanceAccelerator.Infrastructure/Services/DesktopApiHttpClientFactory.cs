using SystemPerformanceAccelerator.Infrastructure.Configuration;

namespace SystemPerformanceAccelerator.Infrastructure.Services;

public sealed class DesktopApiHttpClientFactory
{
    private readonly DesktopApiOptions _options;
    private HttpClient? _httpClient;

    public DesktopApiHttpClientFactory(DesktopApiOptions options)
    {
        _options = options ??
            throw new ArgumentNullException(nameof(options));
    }

    public HttpClient GetOrCreate()
    {
        _httpClient ??= CreateClient(_options);
        return _httpClient;
    }

    internal static HttpClient CreateClient(DesktopApiOptions options) =>
        new()
        {
            BaseAddress = options.ActiveBaseUrl,
            Timeout = Timeout.InfiniteTimeSpan
        };
}

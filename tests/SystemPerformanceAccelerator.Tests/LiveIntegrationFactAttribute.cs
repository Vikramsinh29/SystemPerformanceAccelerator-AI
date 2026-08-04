using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class LiveIntegrationFactAttribute : FactAttribute
{
    private static readonly string[] RequiredVariables =
    [
        "PCSPA_TEST_EMAIL",
        "PCSPA_TEST_PASSWORD",
        "PCSPA_TEST_ACTIVATION_KEY",
        "PCSPA_API_BASE_URL"
    ];

    public LiveIntegrationFactAttribute()
    {
        var missing = RequiredVariables
            .Where(name => string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(name)))
            .ToArray();
        if (missing.Length > 0)
        {
            Skip =
                "Live licensing integration test skipped because required environment variables are missing: " +
                string.Join(", ", missing);
        }
    }
}

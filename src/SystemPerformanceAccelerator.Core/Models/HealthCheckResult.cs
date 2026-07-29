namespace SystemPerformanceAccelerator.Core.Models;

public sealed record HealthCheckResult(
    IReadOnlyList<HealthCheckItem> Items,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAt)
{
    public int GoodCount =>
        Items.Count(item => item.Status == HealthCheckStatus.Good);

    public int AttentionCount =>
        Items.Count(item => item.Status == HealthCheckStatus.Attention);

    public int UnknownCount =>
        Items.Count(item => item.Status == HealthCheckStatus.Unknown);

    public HealthCheckStatus OverallStatus
    {
        get
        {
            if (Items.Any(item => item.Status == HealthCheckStatus.Attention))
            {
                return HealthCheckStatus.Attention;
            }

            if (Items.Count == 0 ||
                Items.Any(item => item.Status == HealthCheckStatus.Unknown))
            {
                return HealthCheckStatus.Unknown;
            }

            return HealthCheckStatus.Good;
        }
    }
}

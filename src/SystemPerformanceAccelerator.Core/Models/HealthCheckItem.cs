namespace SystemPerformanceAccelerator.Core.Models;

public enum HealthCheckStatus
{
    Good,
    Attention,
    Unknown
}

public sealed record HealthCheckItem(
    string Name,
    string Value,
    string Details,
    HealthCheckStatus Status)
{
    public string StatusText => Status switch
    {
        HealthCheckStatus.Good => "Good",
        HealthCheckStatus.Attention => "Attention",
        _ => "Unknown"
    };
}

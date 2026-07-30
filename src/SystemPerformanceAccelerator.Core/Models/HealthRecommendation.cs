namespace SystemPerformanceAccelerator.Core.Models;

public enum HealthRecommendationPriority
{
    Low,
    Medium,
    High
}

public sealed record HealthRecommendation(
    string Area,
    string Title,
    string Recommendation,
    string WhyItMatters,
    HealthRecommendationPriority Priority)
{
    public string PriorityText => Priority switch
    {
        HealthRecommendationPriority.High => "High",
        HealthRecommendationPriority.Medium => "Medium",
        _ => "Low"
    };
}

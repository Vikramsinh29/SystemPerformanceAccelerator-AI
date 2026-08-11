namespace SystemPerformanceAccelerator.Core.Models;

public sealed record AuthSession(
    string? UserId,
    string? Email,
    string? DisplayName,
    bool IsAuthenticated,
    DateTimeOffset? ExpiresUtc);

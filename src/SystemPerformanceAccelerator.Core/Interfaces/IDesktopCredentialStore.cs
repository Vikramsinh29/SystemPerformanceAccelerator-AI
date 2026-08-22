namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IDesktopCredentialStore
{
    Task SaveAsync(
        string bearerToken,
        DateTimeOffset expiresUtc,
        CancellationToken cancellationToken = default);

    Task<DesktopCredential?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task ClearAsync(
        CancellationToken cancellationToken = default);
}

public sealed record DesktopCredential(
    string BearerToken,
    DateTimeOffset ExpiresUtc);
namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IDeviceIdentityProvider
{
    Task<string> GetDeviceIdAsync(
        CancellationToken cancellationToken = default);
}

using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IAuthenticationService
{
    Task<AuthLoginResult> LoginAsync(
        AuthLoginRequest request,
        CancellationToken cancellationToken = default);

    Task<RemoteOperationResult> LogoutAsync(
        CancellationToken cancellationToken = default);

    Task<AuthSessionResult> GetSessionAsync(
        CancellationToken cancellationToken = default);
}

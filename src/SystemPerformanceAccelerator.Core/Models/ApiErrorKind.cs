namespace SystemPerformanceAccelerator.Core.Models;

public enum ApiErrorKind
{
    None = 0,
    InvalidRequest,
    AuthenticationFailed,
    AuthorizationFailed,
    NotFound,
    Conflict,
    ValidationFailed,
    RateLimited,
    Transient,
    NetworkUnavailable,
    Timeout,
    Cancelled,
    UnexpectedResponse,
    ServerError,
    Unknown
}

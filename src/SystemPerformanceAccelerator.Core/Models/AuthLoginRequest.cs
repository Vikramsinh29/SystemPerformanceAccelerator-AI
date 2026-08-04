namespace SystemPerformanceAccelerator.Core.Models;

public sealed record AuthLoginRequest(
    string Email,
    string Password);

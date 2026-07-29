namespace SystemPerformanceAccelerator.Core.Models;

public sealed record SystemDriveSpace(
    string RootPath,
    long TotalBytes,
    long AvailableBytes);

namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairEnvironmentStatus(
    bool IsWindows,
    bool IsElevated,
    string WindowsDescription,
    string WindowsDirectory,
    string SystemDriveRoot,
    bool DismAvailable,
    bool SfcAvailable,
    long? SystemDriveFreeBytes,
    IReadOnlyList<string> Issues);

namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairPlanRuntimeStatus(
    bool IsWindows,
    bool IsElevated,
    bool DismAvailable,
    bool SfcAvailable,
    bool? PendingRestartDetected,
    long? SystemDriveFreeBytes,
    IReadOnlyList<string> Issues);

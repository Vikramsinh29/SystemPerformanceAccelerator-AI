namespace SystemPerformanceAccelerator.Core.Models;

public sealed record DiagnosticEnvironment(
    string ApplicationVersion,
    string BuildIdentifier,
    string WindowsVersion,
    string RuntimeVersion,
    bool IsElevated,
    long? AvailableMemoryBytes,
    long? SystemDriveFreeBytes,
    string? CpuModel,
    long? InstalledMemoryBytes);

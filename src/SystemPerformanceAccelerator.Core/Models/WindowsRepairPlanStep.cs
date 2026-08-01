namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairPlanStep(
    int Sequence,
    string Title,
    string Purpose,
    bool IsProposed,
    bool ChangesWindows,
    bool MayUseWindowsUpdate,
    bool RequiresFreshConsent,
    bool AutomaticRestart);

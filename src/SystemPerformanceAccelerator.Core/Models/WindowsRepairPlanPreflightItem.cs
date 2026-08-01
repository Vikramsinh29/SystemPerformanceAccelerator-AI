namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairPlanPreflightItem(
    string Title,
    WindowsRepairPlanItemStatus Status,
    string Detail);

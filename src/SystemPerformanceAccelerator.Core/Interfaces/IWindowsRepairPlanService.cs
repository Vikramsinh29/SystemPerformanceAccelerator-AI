using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IWindowsRepairPlanService
{
    WindowsRepairPlan CreatePlan(
        WindowsRepairAssessmentResult assessment);
}

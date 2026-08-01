namespace SystemPerformanceAccelerator.Core.Models;

public sealed record WindowsRepairAssessmentRequest(
    bool CheckComponentStore,
    bool VerifyProtectedSystemFiles)
{
    public static WindowsRepairAssessmentRequest Default { get; } =
        new(CheckComponentStore: true, VerifyProtectedSystemFiles: true);

    public bool HasSelectedChecks =>
        CheckComponentStore || VerifyProtectedSystemFiles;

    public IReadOnlyList<WindowsRepairAssessmentCheck> GetSelectedChecks()
    {
        var checks = new List<WindowsRepairAssessmentCheck>(capacity: 2);

        if (CheckComponentStore)
        {
            checks.Add(
                WindowsRepairAssessmentCheck.ComponentStoreCheckHealth);
        }

        if (VerifyProtectedSystemFiles)
        {
            checks.Add(
                WindowsRepairAssessmentCheck.ProtectedSystemFilesVerifyOnly);
        }

        return checks;
    }
}

using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IDiagnosticFeedbackSubmissionService
{
    Task<DiagnosticFeedbackSubmissionResult> SubmitAsync(
        DiagnosticFeedbackSubmissionRequest report,
        CancellationToken cancellationToken = default);
}

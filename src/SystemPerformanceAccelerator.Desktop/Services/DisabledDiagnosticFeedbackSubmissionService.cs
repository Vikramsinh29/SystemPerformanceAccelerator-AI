using SystemPerformanceAccelerator.Core.Interfaces;
using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Desktop.Services;

internal sealed class DisabledDiagnosticFeedbackSubmissionService :
    IDiagnosticFeedbackSubmissionService
{
    public Task<DiagnosticFeedbackSubmissionResult> SubmitAsync(
        DiagnosticFeedbackSubmissionRequest report,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            new DiagnosticFeedbackSubmissionResult(
                false,
                null,
                "Online beta feedback is unavailable. Create the local ZIP instead."));
}

namespace SystemPerformanceAccelerator.Core.Models;

public sealed record DiagnosticEvent(
    string ReferenceId,
    string InstallationId,
    DateTimeOffset TimestampUtc,
    DiagnosticSeverity Severity,
    string Feature,
    string OperationStage,
    string ExceptionType,
    string Message,
    string StackTrace,
    bool Recovered,
    bool UserDataMayHaveBeenAffected,
    DiagnosticEnvironment Environment);

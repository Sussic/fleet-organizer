namespace FleetOrganizer.Core.Abstractions;

public interface IWorkflowDiagnosticLog
{
    Task WriteFailureAsync(
        string action,
        long? fleetId,
        Exception exception,
        CancellationToken cancellationToken = default);
}

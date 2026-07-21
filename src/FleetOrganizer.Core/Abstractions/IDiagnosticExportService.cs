namespace FleetOrganizer.Core.Abstractions;

public interface IDiagnosticExportService
{
    Task ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken = default);
}

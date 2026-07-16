namespace FleetOrganizer.Core.Abstractions;

public interface IAppDataPaths
{
    string RootDirectory { get; }

    string DatabasePath { get; }

    string LogsDirectory { get; }
}

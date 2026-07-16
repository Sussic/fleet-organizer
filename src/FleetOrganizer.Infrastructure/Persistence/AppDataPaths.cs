using FleetOrganizer.Core.Abstractions;

namespace FleetOrganizer.Infrastructure.Persistence;

public sealed class AppDataPaths : IAppDataPaths
{
    public AppDataPaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FleetOrganizer"))
    {
    }

    public AppDataPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        DatabasePath = Path.Combine(RootDirectory, "fleet-organizer.db");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
    }

    public string RootDirectory { get; }

    public string DatabasePath { get; }

    public string LogsDirectory { get; }

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}

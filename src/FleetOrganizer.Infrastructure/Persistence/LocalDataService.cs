using FleetOrganizer.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace FleetOrganizer.Infrastructure.Persistence;

internal sealed class LocalDataService(IAppDataPaths paths) : ILocalDataService
{
    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[]
        {
            paths.DatabasePath,
            $"{paths.DatabasePath}-wal",
            $"{paths.DatabasePath}-shm",
        })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        if (Directory.Exists(paths.LogsDirectory))
        {
            Directory.Delete(paths.LogsDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }
}

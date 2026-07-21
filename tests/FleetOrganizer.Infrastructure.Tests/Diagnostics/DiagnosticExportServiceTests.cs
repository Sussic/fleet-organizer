using System.IO.Compression;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Infrastructure.Diagnostics;
using FleetOrganizer.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetOrganizer.Infrastructure.Tests.Diagnostics;

public sealed class DiagnosticExportServiceTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "FleetOrganizer.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExportRedactsSecretsAndExcludesDatabase()
    {
        var paths = new TestAppDataPaths(testRoot);
        await new SqliteDatabaseInitializer(
                paths,
                NullLogger<SqliteDatabaseInitializer>.Instance)
            .InitializeAsync(CancellationToken.None);
        Directory.CreateDirectory(paths.LogsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(paths.LogsDirectory, "crash.log"),
            $"Authorization: Bearer very.secret.token{Environment.NewLine}" +
            $"refresh_token=never-share-this{Environment.NewLine}" +
            $"path={Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\secret");
        var destination = Path.Combine(testRoot, "support.zip");
        var service = new DiagnosticExportService(
            paths,
            new FleetOperationRepository(paths),
            new FleetDeskPreferencesRepository(paths, TimeProvider.System),
            TimeProvider.System);

        await service.ExportAsync(destination, CancellationToken.None);

        using var archive = ZipFile.OpenRead(destination);
        Assert.DoesNotContain(archive.Entries, entry =>
            entry.FullName.EndsWith(".db", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(archive.Entries, entry => entry.FullName == "environment.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "preferences.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "operation-summaries.json");
        var logEntry = Assert.Single(archive.Entries, entry => entry.FullName == "logs/crash.log");
        using var reader = new StreamReader(logEntry.Open());
        var log = await reader.ReadToEndAsync();
        Assert.DoesNotContain("very.secret.token", log, StringComparison.Ordinal);
        Assert.DoesNotContain("never-share-this", log, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            log,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", log, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class TestAppDataPaths(string rootDirectory) : IAppDataPaths
    {
        public string RootDirectory { get; } = rootDirectory;

        public string DatabasePath { get; } = Path.Combine(rootDirectory, "test.db");

        public string LogsDirectory { get; } = Path.Combine(rootDirectory, "logs");
    }
}

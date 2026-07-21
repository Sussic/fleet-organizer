using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using FleetOrganizer.Core.Abstractions;

namespace FleetOrganizer.Infrastructure.Diagnostics;

internal sealed partial class DiagnosticExportService(
    IAppDataPaths paths,
    IFleetOperationRepository operationRepository,
    IFleetDeskPreferencesRepository preferencesRepository,
    TimeProvider timeProvider) : IDiagnosticExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public async Task ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        await WriteJsonAsync(
            archive,
            "environment.json",
            new
            {
                generatedUtc = timeProvider.GetUtcNow(),
                version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                framework = RuntimeInformation.FrameworkDescription,
                operatingSystem = RuntimeInformation.OSDescription,
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            },
            cancellationToken).ConfigureAwait(false);

        var preferences = await preferencesRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(archive, "preferences.json", preferences, cancellationToken)
            .ConfigureAwait(false);

        var operations = await operationRepository.LoadRecentAsync(50, cancellationToken)
            .ConfigureAwait(false);
        await WriteJsonAsync(
            archive,
            "operation-summaries.json",
            operations.Select(operation => new
            {
                operation.Id,
                operation.ProfileName,
                operation.FleetId,
                state = operation.State.ToString(),
                operation.CreatedAtUtc,
                operation.UpdatedAtUtc,
                operation.SucceededSteps,
                operation.FailedSteps,
                operation.SkippedSteps,
                operation.Message,
                failures = operation.Steps
                    .Where(step => step.LastFailureKind is not null)
                    .Select(step => new
                    {
                        step.StepKey,
                        type = step.Type.ToString(),
                        step.Attempts,
                        step.LastFailureKind,
                        step.Message,
                    }),
            }),
            cancellationToken).ConfigureAwait(false);

        if (Directory.Exists(paths.LogsDirectory))
        {
            foreach (var logPath in Directory.EnumerateFiles(paths.LogsDirectory, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(5))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = await File.ReadAllTextAsync(logPath, cancellationToken).ConfigureAwait(false);
                await WriteTextAsync(
                    archive,
                    $"logs/{Path.GetFileName(logPath)}",
                    Redact(text),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await WriteTextAsync(
            archive,
            "README.txt",
            "This Fleet Desk support bundle excludes the SQLite database, access/refresh tokens, the EVE client ID, and local absolute user paths. Review it before sharing because fleet/profile names and failure messages remain useful for diagnosis.",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        CancellationToken cancellationToken) =>
        await WriteTextAsync(
            archive,
            entryName,
            Redact(JsonSerializer.Serialize(value, JsonOptions)),
            cancellationToken).ConfigureAwait(false);

    private static async Task WriteTextAsync(
        ZipArchive archive,
        string entryName,
        string value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream);
        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteAsync(value).ConfigureAwait(false);
    }

    private static string Redact(string value)
    {
        var redacted = BearerPattern().Replace(value, "Bearer [REDACTED]");
        redacted = SecretPattern().Replace(redacted, "$1$2[REDACTED]");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? redacted
            : redacted.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("(?i)(access[_ -]?token|refresh[_ -]?token|client[_ -]?(?:id|secret)|authorization)(\\s*[:=]\\s*[\\\"]?)[^\\s\\\",]+")]
    private static partial Regex SecretPattern();

    [GeneratedRegex("(?i)Bearer\\s+[A-Za-z0-9._~+/-]+={0,2}")]
    private static partial Regex BearerPattern();
}

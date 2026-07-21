using System.Globalization;
using System.Text.RegularExpressions;
using FleetOrganizer.Core.Abstractions;

namespace FleetOrganizer.Infrastructure.Diagnostics;

internal sealed partial class WorkflowDiagnosticLog(
    IAppDataPaths paths,
    TimeProvider timeProvider) : IWorkflowDiagnosticLog, IDisposable
{
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public async Task WriteFailureAsync(
        string action,
        long? fleetId,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(exception);
        var now = timeProvider.GetUtcNow();
        var path = Path.Combine(paths.LogsDirectory, $"workflow-{now:yyyyMMdd}.log");
        var message = Redact(exception.Message.ReplaceLineEndings(" "));
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{now:O}\taction={Redact(action)}\tfleet={fleetId?.ToString(CultureInfo.InvariantCulture) ?? "none"}\ttype={exception.GetType().Name}\tmessage={message}{Environment.NewLine}");

        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            await File.AppendAllTextAsync(path, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public void Dispose()
    {
        writeGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string Redact(string value)
    {
        var redacted = SecretPattern().Replace(value, "$1=[REDACTED]");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? redacted
            : redacted.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("(?i)(access[_ -]?token|refresh[_ -]?token|client[_ -]?(?:id|secret)|authorization)\\s*[:=]\\s*[^\\s,;]+")]
    private static partial Regex SecretPattern();
}

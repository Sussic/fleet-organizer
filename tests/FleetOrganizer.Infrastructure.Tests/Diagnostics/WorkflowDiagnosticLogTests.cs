using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Infrastructure.Diagnostics;

namespace FleetOrganizer.Infrastructure.Tests.Diagnostics;

public sealed class WorkflowDiagnosticLogTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "FleetOrganizer.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteFailureCreatesARedactedSupportLog()
    {
        var paths = new TestAppDataPaths(testRoot);
        using var log = new WorkflowDiagnosticLog(paths, TimeProvider.System);

        await log.WriteFailureAsync(
            "quick-invite",
            7001,
            new InvalidOperationException("access_token=secret-value failed"),
            CancellationToken.None);

        var path = Assert.Single(Directory.GetFiles(paths.LogsDirectory, "workflow-*.log"));
        var text = await File.ReadAllTextAsync(path, CancellationToken.None);
        Assert.Contains("action=quick-invite", text, StringComparison.Ordinal);
        Assert.Contains("fleet=7001", text, StringComparison.Ordinal);
        Assert.Contains("access_token=[REDACTED]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", text, StringComparison.Ordinal);
    }

    public void Dispose()
    {
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

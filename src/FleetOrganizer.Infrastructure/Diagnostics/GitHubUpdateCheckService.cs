using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using FleetOrganizer.Core.Abstractions;

namespace FleetOrganizer.Infrastructure.Diagnostics;

internal sealed class GitHubUpdateCheckService(HttpClient httpClient) : IUpdateCheckService
{
    private static readonly Uri LatestReleaseUri = new(
        "https://api.github.com/repos/Sussic/fleet-organizer/releases/latest");

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
        using var response = await httpClient.GetAsync(LatestReleaseUri, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(
                false,
                FormatVersion(current),
                null,
                null,
                "No stable public Fleet Organizer release exists yet. You are running a development build.");
        }

        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException(
                "GitHub returned an empty release response.");
        var latestText = release.TagName.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(latestText, out var latest))
        {
            throw new InvalidOperationException(
                $"GitHub returned an unsupported release version '{release.TagName}'.");
        }

        var isAvailable = latest > current;
        return new UpdateCheckResult(
            isAvailable,
            FormatVersion(current),
            FormatVersion(latest),
            release.HtmlUrl,
            isAvailable
                ? $"Fleet Organizer {FormatVersion(latest)} is available. Opening the release page never installs anything automatically."
                : $"Fleet Organizer {FormatVersion(current)} is up to date with the latest stable release.");
    }

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl);
}

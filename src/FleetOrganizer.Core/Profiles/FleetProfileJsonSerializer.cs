using System.Text.Json;
using System.Text.Json.Serialization;
using FleetOrganizer.Core.Domain;

namespace FleetOrganizer.Core.Profiles;

public static class FleetProfileJsonSerializer
{
    private const int CurrentExportVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(FleetProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return JsonSerializer.Serialize(
            new FleetProfileExport(CurrentExportVersion, profile),
            SerializerOptions);
    }

    public static FleetProfile Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var document = JsonSerializer.Deserialize<FleetProfileExport>(json, SerializerOptions)
            ?? throw new InvalidOperationException("The profile file is empty or invalid.");
        if (document.ExportVersion != CurrentExportVersion)
        {
            throw new InvalidOperationException(
                $"Profile export version {document.ExportVersion} is not supported.");
        }

        return document.Profile ?? throw new InvalidOperationException(
            "The profile file does not contain a profile.");
    }

    private sealed record FleetProfileExport(int ExportVersion, FleetProfile? Profile);
}

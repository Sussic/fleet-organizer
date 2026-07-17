using FleetOrganizer.Core.Planning;

namespace FleetOrganizer.Core.Profiles;

public sealed record FleetDeskPreferences
{
    public Guid? LastUsedProfileId { get; init; }

    public Guid? DefaultProfileId { get; init; }

    public Guid[] PinnedProfileIds { get; init; } = [];

    public FleetRunMode RunMode { get; init; } = FleetRunMode.FullOrganise;

    public bool AttentionSoundsEnabled { get; init; } = true;
}

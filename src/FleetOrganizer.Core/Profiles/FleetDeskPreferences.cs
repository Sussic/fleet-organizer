using FleetOrganizer.Core.Planning;

namespace FleetOrganizer.Core.Profiles;

public enum FleetDeskTheme
{
    System,
    Light,
    Dark,
}

public sealed record FleetDeskPreferences
{
    public Guid? LastUsedProfileId { get; init; }

    public Guid? DefaultProfileId { get; init; }

    public Guid[] PinnedProfileIds { get; init; } = [];

    public FleetRunMode RunMode { get; init; } = FleetRunMode.FullOrganise;

    public bool AttentionSoundsEnabled { get; init; } = true;

    public int FleetPollingSeconds { get; init; } = 30;

    public int InvitationCheckSeconds { get; init; } = 30;

    public int InvitationTimeoutMinutes { get; init; } = 10;

    public bool StartMinimized { get; init; }

    public bool MinimizeToTray { get; init; }

    public FleetDeskTheme Theme { get; init; } = FleetDeskTheme.System;
}

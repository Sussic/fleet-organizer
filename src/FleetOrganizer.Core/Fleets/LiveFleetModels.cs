namespace FleetOrganizer.Core.Fleets;

public enum LiveFleetLoadStatus
{
    Ready,
    SignedOut,
    NotInFleet,
    NotFleetBoss,
    Failed,
}

public sealed record LiveFleetMember(
    long CharacterId,
    string CharacterName,
    string Role,
    string RoleName,
    long ShipTypeId,
    string ShipTypeName,
    long SolarSystemId,
    string SolarSystemName,
    long? StationId,
    string? StationName,
    long WingId,
    long SquadId,
    DateTimeOffset JoinTime,
    bool TakesFleetWarp);

public sealed record LiveFleetSquad(
    long SquadId,
    string Name);

public sealed record LiveFleetWing(
    long WingId,
    string Name,
    LiveFleetSquad[] Squads);

public sealed record LiveFleetSnapshot(
    long FleetId,
    long FleetBossId,
    string FleetBossName,
    bool IsFleetBoss,
    bool IsFreeMove,
    bool IsRegistered,
    bool IsVoiceEnabled,
    string Motd,
    DateTimeOffset ConfirmedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    LiveFleetWing[] Wings,
    LiveFleetMember[] Members);

public sealed record LiveFleetLoadResult(
    LiveFleetLoadStatus Status,
    LiveFleetSnapshot? Snapshot,
    string UserMessage,
    TimeSpan? RetryAfter,
    DateTimeOffset? CheckedAtUtc);

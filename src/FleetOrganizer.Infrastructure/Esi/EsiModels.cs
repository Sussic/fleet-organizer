using System.Net;
using System.Text.Json.Serialization;

namespace FleetOrganizer.Infrastructure.Esi;

internal enum EsiFailureKind
{
    None,
    Unauthorized,
    Forbidden,
    NotFound,
    ErrorLimited,
    RateLimited,
    Validation,
    Server,
    Network,
    InvalidResponse,
    Client,
    Paused,
}

internal sealed record EsiRateState(
    string? Group,
    string? Limit,
    int? Remaining,
    int? Used,
    int? ErrorLimitRemaining,
    int? ErrorLimitResetSeconds);

internal sealed record EsiResult<T>(
    T? Value,
    HttpStatusCode StatusCode,
    EsiFailureKind FailureKind,
    string? UserMessage,
    string? RequestId,
    DateTimeOffset FetchedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? ETag,
    DateTimeOffset? LastModifiedUtc,
    TimeSpan? RetryAfter,
    EsiRateState? RateState,
    bool FromCache)
{
    public bool IsSuccess => FailureKind == EsiFailureKind.None && Value is not null;
}

internal sealed record EsiEmptyResponse;

#pragma warning disable CA1812 // System.Text.Json creates these internal OpenAPI contract records.
internal sealed record CharacterFleetResponse(
    [property: JsonPropertyName("fleet_boss_id")] long FleetBossId,
    [property: JsonPropertyName("fleet_id")] long FleetId,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("squad_id")] long SquadId,
    [property: JsonPropertyName("wing_id")] long WingId);

internal sealed record FleetInfoResponse(
    [property: JsonPropertyName("is_free_move")] bool IsFreeMove,
    [property: JsonPropertyName("is_registered")] bool IsRegistered,
    [property: JsonPropertyName("is_voice_enabled")] bool IsVoiceEnabled,
    [property: JsonPropertyName("motd")] string Motd);

internal sealed record UpdateFleetRequest(
    [property: JsonPropertyName("is_free_move")] bool IsFreeMove,
    [property: JsonPropertyName("motd")] string Motd);

internal sealed record FleetMemberResponse(
    [property: JsonPropertyName("character_id")] long CharacterId,
    [property: JsonPropertyName("join_time")] DateTimeOffset JoinTime,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("role_name")] string RoleName,
    [property: JsonPropertyName("ship_type_id")] long ShipTypeId,
    [property: JsonPropertyName("solar_system_id")] long SolarSystemId,
    [property: JsonPropertyName("squad_id")] long SquadId,
    [property: JsonPropertyName("station_id")] long? StationId,
    [property: JsonPropertyName("takes_fleet_warp")] bool TakesFleetWarp,
    [property: JsonPropertyName("wing_id")] long WingId);

internal sealed record FleetSquadResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name);

internal sealed record FleetWingResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("squads")] FleetSquadResponse[] Squads);

internal sealed record UniverseNameResponse(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name);

internal sealed record UniverseIdNameResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name);

internal sealed record UniverseIdsResponse(
    [property: JsonPropertyName("characters")] UniverseIdNameResponse[]? Characters);

internal sealed record InviteFleetMemberRequest(
    [property: JsonPropertyName("character_id")] long CharacterId,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("squad_id")] long SquadId,
    [property: JsonPropertyName("wing_id")] long WingId);

internal sealed record CreatedFleetWingResponse(
    [property: JsonPropertyName("wing_id")] long WingId);

internal sealed record CreatedFleetSquadResponse(
    [property: JsonPropertyName("squad_id")] long SquadId);

internal sealed record RenameFleetStructureRequest(
    [property: JsonPropertyName("name")] string Name);

internal sealed record MoveFleetMemberRequest(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("squad_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? SquadId,
    [property: JsonPropertyName("wing_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? WingId);
#pragma warning restore CA1812

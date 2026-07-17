using System.Globalization;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Authentication;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Infrastructure.Authentication;

namespace FleetOrganizer.Infrastructure.Esi;

internal sealed class EveFleetService : ILiveFleetService, IDisposable
{
    private readonly EveEsiClient esiClient;
    private readonly IEveAuthenticationService authenticationService;
    private readonly Dictionary<long, string> nameCache = [];
    private readonly SemaphoreSlim loadGate = new(1, 1);

    public EveFleetService(
        EveEsiClient esiClient,
        IEveAuthenticationService authenticationService)
    {
        ArgumentNullException.ThrowIfNull(esiClient);
        ArgumentNullException.ThrowIfNull(authenticationService);

        this.esiClient = esiClient;
        this.authenticationService = authenticationService;
    }

    public async Task<LiveFleetLoadResult> LoadCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        await loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var character = authenticationService.CurrentCharacter;
            if (character is null)
            {
                return new LiveFleetLoadResult(
                    LiveFleetLoadStatus.SignedOut,
                    null,
                    "Sign in with the character that will be fleet boss.",
                    null,
                    null);
            }

            nameCache[character.CharacterId] = character.CharacterName;
            return await LoadCurrentCoreAsync(
                character,
                allowFleetRedetection: true,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            loadGate.Release();
        }
    }

    public async Task<LiveFleetLoadResult> RefreshCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        await loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var character = authenticationService.CurrentCharacter;
            if (character is null)
            {
                return new LiveFleetLoadResult(
                    LiveFleetLoadStatus.SignedOut,
                    null,
                    "Sign in with the character that will be fleet boss.",
                    null,
                    null);
            }

            esiClient.InvalidateCharacterFleet(character.CharacterId);
            var detection = await esiClient
                .GetCharacterFleetAsync(character.CharacterId, cancellationToken)
                .ConfigureAwait(false);
            if (detection.IsSuccess && detection.Value is not null)
            {
                esiClient.InvalidateLiveFleet(detection.Value.FleetId);
            }

            nameCache[character.CharacterId] = character.CharacterName;
            return await LoadCurrentCoreAsync(
                character,
                allowFleetRedetection: true,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            loadGate.Release();
        }
    }

    public void Dispose()
    {
        loadGate.Dispose();
    }

    private async Task<LiveFleetLoadResult> LoadCurrentCoreAsync(
        AuthenticatedCharacter character,
        bool allowFleetRedetection,
        CancellationToken cancellationToken)
    {
        var detection = await esiClient
            .GetCharacterFleetAsync(character.CharacterId, cancellationToken)
            .ConfigureAwait(false);

        if (!detection.IsSuccess)
        {
            return detection.FailureKind == EsiFailureKind.NotFound
                ? new LiveFleetLoadResult(
                    LiveFleetLoadStatus.NotInFleet,
                    null,
                    "This character is not currently in an EVE fleet. Create or join a fleet in EVE, then refresh.",
                    null,
                    detection.FetchedAtUtc)
                : CreateFailureResult(detection);
        }

        var detectedFleet = detection.Value ?? throw new InvalidOperationException(
            "ESI reported success without current fleet data.");
        await ResolveNamesAsync([detectedFleet.FleetBossId], cancellationToken).ConfigureAwait(false);

        if (detectedFleet.FleetBossId != character.CharacterId)
        {
            var snapshot = new LiveFleetSnapshot(
                detectedFleet.FleetId,
                detectedFleet.FleetBossId,
                GetName(detectedFleet.FleetBossId, "Character"),
                IsFleetBoss: false,
                IsFreeMove: false,
                IsRegistered: false,
                IsVoiceEnabled: false,
                Motd: string.Empty,
                detection.FetchedAtUtc,
                detection.ExpiresAtUtc,
                [],
                []);
            return new LiveFleetLoadResult(
                LiveFleetLoadStatus.NotFleetBoss,
                snapshot,
                $"{character.CharacterName} is in fleet, but {snapshot.FleetBossName} is the fleet boss. Make the signed-in character fleet boss in EVE, then refresh.",
                null,
                snapshot.ConfirmedAtUtc);
        }

        var fleet = await esiClient
            .GetFleetAsync(detectedFleet.FleetId, cancellationToken)
            .ConfigureAwait(false);
        if (!fleet.IsSuccess)
        {
            return await HandleFleetReadFailureAsync(
                fleet,
                character,
                allowFleetRedetection,
                cancellationToken).ConfigureAwait(false);
        }

        var members = await esiClient
            .GetFleetMembersAsync(detectedFleet.FleetId, cancellationToken)
            .ConfigureAwait(false);
        if (!members.IsSuccess)
        {
            return await HandleFleetReadFailureAsync(
                members,
                character,
                allowFleetRedetection,
                cancellationToken).ConfigureAwait(false);
        }

        var wings = await esiClient
            .GetFleetWingsAsync(detectedFleet.FleetId, cancellationToken)
            .ConfigureAwait(false);
        if (!wings.IsSuccess)
        {
            return await HandleFleetReadFailureAsync(
                wings,
                character,
                allowFleetRedetection,
                cancellationToken).ConfigureAwait(false);
        }

        var fleetValue = fleet.Value ?? throw new InvalidOperationException(
            "ESI reported success without fleet settings.");
        var memberValues = members.Value ?? throw new InvalidOperationException(
            "ESI reported success without fleet members.");
        var wingValues = wings.Value ?? throw new InvalidOperationException(
            "ESI reported success without fleet wings.");

        await ResolveMemberNamesAsync(memberValues, cancellationToken).ConfigureAwait(false);

        var snapshotReady = new LiveFleetSnapshot(
            detectedFleet.FleetId,
            detectedFleet.FleetBossId,
            GetName(detectedFleet.FleetBossId, "Character"),
            IsFleetBoss: true,
            fleetValue.IsFreeMove,
            fleetValue.IsRegistered,
            fleetValue.IsVoiceEnabled,
            fleetValue.Motd,
            Latest(fleet.FetchedAtUtc, members.FetchedAtUtc, wings.FetchedAtUtc),
            Earliest(fleet.ExpiresAtUtc, members.ExpiresAtUtc, wings.ExpiresAtUtc),
            wingValues.Select(MapWing).ToArray(),
            memberValues.Select(MapMember).ToArray());

        return new LiveFleetLoadResult(
            LiveFleetLoadStatus.Ready,
            snapshotReady,
            $"Live fleet loaded: {snapshotReady.Members.Length} member{(snapshotReady.Members.Length == 1 ? string.Empty : "s")} across {snapshotReady.Wings.Length} wing{(snapshotReady.Wings.Length == 1 ? string.Empty : "s")}.",
            null,
            snapshotReady.ConfirmedAtUtc);
    }

    private async Task<LiveFleetLoadResult> HandleFleetReadFailureAsync<T>(
        EsiResult<T> result,
        AuthenticatedCharacter character,
        bool allowFleetRedetection,
        CancellationToken cancellationToken)
    {
        if (result.FailureKind == EsiFailureKind.NotFound && allowFleetRedetection)
        {
            esiClient.InvalidateCharacterFleet(character.CharacterId);
            return await LoadCurrentCoreAsync(
                character,
                allowFleetRedetection: false,
                cancellationToken).ConfigureAwait(false);
        }

        return CreateFailureResult(result);
    }

    private async Task ResolveMemberNamesAsync(
        FleetMemberResponse[] members,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<long>();
        foreach (var member in members)
        {
            ids.Add(member.CharacterId);
            ids.Add(member.ShipTypeId);
            ids.Add(member.SolarSystemId);
            if (member.StationId is long stationId)
            {
                ids.Add(stationId);
            }
        }

        await ResolveNamesAsync(ids.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private async Task ResolveNamesAsync(long[] ids, CancellationToken cancellationToken)
    {
        var unresolvedIds = ids
            .Where(id => id > 0 && !nameCache.ContainsKey(id))
            .Distinct()
            .ToArray();

        foreach (var batch in unresolvedIds.Chunk(1000))
        {
            var result = await esiClient
                .PostUniverseNamesAsync(batch, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is null)
            {
                continue;
            }

            foreach (var resolvedName in result.Value)
            {
                nameCache[resolvedName.Id] = resolvedName.Name;
            }
        }
    }

    private LiveFleetMember MapMember(FleetMemberResponse member) =>
        new(
            member.CharacterId,
            GetName(member.CharacterId, "Character"),
            member.Role,
            string.IsNullOrWhiteSpace(member.RoleName)
                ? HumanizeRole(member.Role)
                : member.RoleName,
            member.ShipTypeId,
            GetName(member.ShipTypeId, "Type"),
            member.SolarSystemId,
            GetName(member.SolarSystemId, "System"),
            member.StationId,
            member.StationId is long stationId ? GetName(stationId, "Station") : null,
            member.WingId,
            member.SquadId,
            member.JoinTime,
            member.TakesFleetWarp);

    private static LiveFleetWing MapWing(FleetWingResponse wing) =>
        new(
            wing.Id,
            wing.Name,
            wing.Squads
                .Select(squad => new LiveFleetSquad(squad.Id, squad.Name))
                .ToArray());

    private string GetName(long id, string category) =>
        nameCache.TryGetValue(id, out var name)
            ? name
            : $"{category} {id}";

    private static string HumanizeRole(string role) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(role.Replace('_', ' '));

    private static LiveFleetLoadResult CreateFailureResult<T>(EsiResult<T> result) =>
        new(
            LiveFleetLoadStatus.Failed,
            null,
            result.UserMessage ?? "The live fleet could not be loaded.",
            result.RetryAfter,
            result.FetchedAtUtc);

    private static DateTimeOffset Latest(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third) =>
        new[] { first, second, third }.Max();

    private static DateTimeOffset? Earliest(
        DateTimeOffset? first,
        DateTimeOffset? second,
        DateTimeOffset? third)
    {
        var values = new[] { first, second, third }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return values.Length == 0 ? null : values.Min();
    }
}

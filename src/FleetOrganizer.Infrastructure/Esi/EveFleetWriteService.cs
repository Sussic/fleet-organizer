using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Operations;

namespace FleetOrganizer.Infrastructure.Esi;

internal sealed class EveFleetWriteService(
    EveEsiClient esiClient) : IFleetWriteService
{
    public async Task<FleetWriteResult> CreateWingAsync(
        long fleetId,
        CancellationToken cancellationToken = default)
    {
        var result = await esiClient
            .CreateFleetWingAsync(fleetId, cancellationToken)
            .ConfigureAwait(false);
        return MapResult(
            result,
            "EVE could not create the wing. The fleet structure or permissions may have changed.",
            result.Value?.WingId);
    }

    public async Task<FleetWriteResult> RenameWingAsync(
        long fleetId,
        FleetOperationTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var result = await esiClient.RenameFleetWingAsync(
            fleetId,
            target.WingId,
            new RenameFleetStructureRequest(target.WingName),
            cancellationToken).ConfigureAwait(false);
        return MapResult(
            result,
            $"EVE could not rename the wing to '{target.WingName}'. Refresh the hierarchy before retrying.");
    }

    public async Task<FleetWriteResult> CreateSquadAsync(
        long fleetId,
        FleetOperationTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var result = await esiClient.CreateFleetSquadAsync(
            fleetId,
            target.WingId,
            cancellationToken).ConfigureAwait(false);
        return MapResult(
            result,
            $"EVE could not create a squad under '{target.WingName}'. Refresh the hierarchy before retrying.",
            result.Value?.SquadId);
    }

    public async Task<FleetWriteResult> RenameSquadAsync(
        long fleetId,
        FleetOperationTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var result = await esiClient.RenameFleetSquadAsync(
            fleetId,
            target.SquadId,
            new RenameFleetStructureRequest(target.SquadName),
            cancellationToken).ConfigureAwait(false);
        return MapResult(
            result,
            $"EVE could not rename the squad to '{target.SquadName}'. Refresh the hierarchy before retrying.");
    }

    public async Task<FleetWriteResult> InviteAsync(
        long fleetId,
        FleetOperationTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var placement = GetPlacement(target.DesiredRole, target.WingId, target.SquadId);
        var result = await esiClient.InviteFleetMemberAsync(
            fleetId,
            new InviteFleetMemberRequest(
                target.CharacterId,
                placement.Role,
                placement.SquadId,
                placement.WingId),
            cancellationToken).ConfigureAwait(false);
        return MapResult(
            result,
            result.FailureKind == EsiFailureKind.Validation
                ? $"EVE rejected the invitation for {target.CharacterName}. The character may have CSPA enabled, may already have an invitation, or the fleet position changed."
                : null);
    }

    private static (string Role, long? SquadId, long? WingId) GetPlacement(
        DesiredFleetRole desiredRole,
        long wingId,
        long squadId) => desiredRole switch
    {
        DesiredFleetRole.FleetCommander => ("fleet_commander", null, null),
        DesiredFleetRole.WingCommander => ("wing_commander", null, wingId),
        DesiredFleetRole.SquadCommander => ("squad_commander", squadId, wingId),
        _ => ("squad_member", squadId, wingId),
    };

    public async Task<FleetWriteResult> PlaceAsync(
        long fleetId,
        FleetOperationTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var result = await esiClient.MoveFleetMemberAsync(
            fleetId,
            target.CharacterId,
            new MoveFleetMemberRequest(
                "squad_member",
                target.SquadId,
                target.WingId),
            cancellationToken).ConfigureAwait(false);
        return MapResult(
            result,
            result.FailureKind == EsiFailureKind.Validation
                ? $"EVE could not place {target.CharacterName}. The member or target squad may have changed; refresh and retry."
                : null);
    }

    public async Task<FleetWriteResult> PromoteCommanderAsync(
        long fleetId,
        FleetOperationTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var request = target.DesiredRole switch
        {
            DesiredFleetRole.SquadCommander => new MoveFleetMemberRequest(
                "squad_commander",
                target.SquadId,
                target.WingId),
            DesiredFleetRole.WingCommander => new MoveFleetMemberRequest(
                "wing_commander",
                SquadId: null,
                WingId: target.WingId),
            _ => throw new InvalidOperationException(
                "Only squad and wing commander transitions are enabled."),
        };
        var result = await esiClient.MoveFleetMemberAsync(
            fleetId,
            target.CharacterId,
            request,
            cancellationToken).ConfigureAwait(false);
        return MapResult(
            result,
            result.FailureKind == EsiFailureKind.Validation
                ? $"EVE could not promote {target.CharacterName}. The commander slot or member position changed; refresh before retrying."
                : null);
    }

    private static FleetWriteResult MapResult<T>(
        EsiResult<T> result,
        string? validationMessage,
        long? createdId = null) =>
        new(
            result.IsSuccess,
            MapFailureKind(result.FailureKind),
            result.IsSuccess
                ? "ESI accepted the fleet write."
                : result.FailureKind == EsiFailureKind.Validation &&
                    !string.IsNullOrWhiteSpace(validationMessage)
                        ? validationMessage
                        : result.UserMessage ?? "ESI rejected the fleet write.",
            result.RequestId,
            result.RetryAfter,
            createdId);

    private static FleetWriteFailureKind MapFailureKind(EsiFailureKind failureKind) =>
        failureKind switch
        {
            EsiFailureKind.None => FleetWriteFailureKind.None,
            EsiFailureKind.Unauthorized => FleetWriteFailureKind.Unauthorized,
            EsiFailureKind.Forbidden => FleetWriteFailureKind.Forbidden,
            EsiFailureKind.NotFound => FleetWriteFailureKind.NotFound,
            EsiFailureKind.Validation => FleetWriteFailureKind.Validation,
            EsiFailureKind.ErrorLimited or
            EsiFailureKind.RateLimited or
            EsiFailureKind.Paused => FleetWriteFailureKind.RateLimited,
            EsiFailureKind.Server => FleetWriteFailureKind.Server,
            EsiFailureKind.Network => FleetWriteFailureKind.Network,
            EsiFailureKind.InvalidResponse => FleetWriteFailureKind.InvalidResponse,
            _ => FleetWriteFailureKind.Client,
        };
}

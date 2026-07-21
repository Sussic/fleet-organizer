using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;

namespace FleetOrganizer.App.ViewModels;

public enum LiveFleetApplyReadiness
{
    Ready,
    NoQueuedChanges,
    Busy,
}

public static class LiveFleetApplyPolicy
{
    public static LiveFleetApplyReadiness CheckBeforePreparation(
        bool hasSnapshot,
        bool hasQueuedChanges,
        bool isBusy)
    {
        if (!hasSnapshot || !hasQueuedChanges)
        {
            return LiveFleetApplyReadiness.NoQueuedChanges;
        }

        return isBusy ? LiveFleetApplyReadiness.Busy : LiveFleetApplyReadiness.Ready;
    }

    public static string? GetPreparedRunBlocker(
        bool canStart,
        bool hasActiveOperation,
        bool isOperationBusy,
        string blockingDetails,
        string statusMessage)
    {
        if (canStart)
        {
            return null;
        }

        if (hasActiveOperation)
        {
            return "A previous fleet run is still active. Open Activity to finish, retry, or cancel it before applying another change.";
        }

        if (isOperationBusy)
        {
            return "Fleet Desk is finishing another saved operation. Try Apply again when it completes.";
        }

        return string.IsNullOrWhiteSpace(blockingDetails) ? statusMessage : blockingDetails;
    }
}

public sealed record LiveFleetReconciliationResult(
    int AcceptedInvites,
    int DepartedMoves,
    int CompletedMoves);

public static class LiveFleetReconciliation
{
    public static LiveFleetReconciliationResult Apply(
        LiveFleetPendingChanges pending,
        LiveFleetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(snapshot);
        var liveCharacterIds = snapshot.Members.Select(member => member.CharacterId).ToHashSet();
        var accepted = pending.Invites
            .Where(invite => liveCharacterIds.Contains(invite.CharacterId))
            .ToArray();
        foreach (var invite in accepted)
        {
            pending.RemoveInvite(invite);
        }

        var departed = pending.Moves
            .Where(move => !liveCharacterIds.Contains(move.CharacterId))
            .ToArray();
        foreach (var move in departed)
        {
            pending.RemoveMove(move);
        }

        var completed = pending.Moves
            .Where(move => snapshot.Members.Any(member => IsApplied(move, member)))
            .ToArray();
        foreach (var move in completed)
        {
            pending.RemoveMove(move);
        }

        return new LiveFleetReconciliationResult(
            accepted.Length,
            departed.Length,
            completed.Length);
    }

    private static bool IsApplied(StagedLiveMoveViewModel move, LiveFleetMember member) =>
        member.CharacterId == move.CharacterId &&
        member.WingId == move.TargetWingId &&
        ToDesiredRole(member.Role) == move.DesiredRole &&
        (move.DesiredRole == DesiredFleetRole.WingCommander ||
            member.SquadId == move.TargetSquadId);

    private static DesiredFleetRole ToDesiredRole(string? role) => role switch
    {
        "squad_commander" => DesiredFleetRole.SquadCommander,
        "wing_commander" => DesiredFleetRole.WingCommander,
        "fleet_commander" => DesiredFleetRole.FleetCommander,
        _ => DesiredFleetRole.SquadMember,
    };
}

public static class LiveFleetOccupancyPolicy
{
    public static bool IsSquadActuallyEmpty(LiveFleetSnapshot snapshot, long squadId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Members.All(member => member.SquadId != squadId);
    }

    public static bool IsWingActuallyEmpty(LiveFleetSnapshot snapshot, long wingId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Members.All(member => member.WingId != wingId);
    }
}

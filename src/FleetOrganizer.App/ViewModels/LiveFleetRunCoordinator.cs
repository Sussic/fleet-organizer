using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Planning;

namespace FleetOrganizer.App.ViewModels;

public sealed record PreparedLiveFleetRun(
    FleetProfile Profile,
    FleetDryRunPlan Plan,
    int RequestedChangeCount);

public interface ILiveFleetRunCoordinator
{
    Task<PreparedLiveFleetRun> PrepareAsync(
        LiveFleetSnapshot snapshot,
        IReadOnlyCollection<StagedLiveMoveViewModel> stagedMoves,
        IReadOnlyCollection<StagedLiveInviteViewModel> stagedInvites,
        IReadOnlyCollection<StagedLiveStructureChangeViewModel> stagedStructureChanges,
        CancellationToken cancellationToken = default);
}

public sealed class LiveFleetRunCoordinator(
    IFleetProfileRepository repository) : ILiveFleetRunCoordinator
{
    private static readonly Guid InternalLiveDeskProfileId =
        Guid.Parse("8f566259-0c06-4e48-a210-e999b5dd271a");

    public async Task<PreparedLiveFleetRun> PrepareAsync(
        LiveFleetSnapshot snapshot,
        IReadOnlyCollection<StagedLiveMoveViewModel> stagedMoves,
        IReadOnlyCollection<StagedLiveInviteViewModel> stagedInvites,
        IReadOnlyCollection<StagedLiveStructureChangeViewModel> stagedStructureChanges,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(stagedMoves);
        ArgumentNullException.ThrowIfNull(stagedInvites);
        ArgumentNullException.ThrowIfNull(stagedStructureChanges);

        var requestedChangeCount =
            stagedMoves.Count + stagedInvites.Count + stagedStructureChanges.Count;
        if (requestedChangeCount == 0)
        {
            throw new InvalidOperationException("Stage at least one live-fleet change first.");
        }

        var profile = LiveFleetProfileComposer.Compose(
            InternalLiveDeskProfileId,
            snapshot,
            stagedMoves,
            stagedInvites,
            stagedStructureChanges);
        await repository.SaveInternalAsync(profile, cancellationToken);

        var mode = stagedInvites.Count == 0 && stagedStructureChanges.Count == 0
            ? FleetRunMode.ApplyLiveChanges
            : FleetRunMode.FullOrganise;
        var plan = FleetPlanModeFilter.Apply(
            FleetPlanner.Build(profile, snapshot),
            mode);
        return new PreparedLiveFleetRun(profile, plan, requestedChangeCount);
    }
}

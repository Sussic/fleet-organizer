using FleetOrganizer.App.ViewModels;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;

namespace FleetOrganizer.Infrastructure.Tests.Ui;

public sealed class LiveFleetPendingChangesTests
{
    [Fact]
    public void UndoUsesActualQueueOrderAcrossMoveAndStructureChanges()
    {
        var state = new LiveFleetPendingChanges();
        var structure = new StagedLiveStructureChangeViewModel(
            Guid.NewGuid(),
            StagedLiveStructureChangeKind.AddWing,
            0,
            0,
            string.Empty,
            string.Empty,
            "Scouts");
        var move = Move(42);

        state.AddStructureChange(structure);
        state.AddMove(move);

        Assert.Equal(move.Summary, state.UndoLastQueuedChange());
        Assert.Empty(state.Moves);
        Assert.Single(state.StructureChanges);
        Assert.Equal(structure.Summary, state.UndoLastQueuedChange());
        Assert.False(state.HasQueuedChanges);
    }

    [Fact]
    public void ReplacingCharacterMoveLeavesOneCurrentUndoEntry()
    {
        var state = new LiveFleetPendingChanges();
        state.AddMove(Move(42, targetSquadId: 20));
        var replacement = Move(42, targetSquadId: 21);

        state.AddMove(replacement);

        Assert.Same(replacement, Assert.Single(state.Moves));
        Assert.Equal(replacement.Summary, state.UndoLastQueuedChange());
        Assert.Null(state.UndoLastQueuedChange());
    }

    private static StagedLiveMoveViewModel Move(long characterId, long targetSquadId = 20) =>
        new(
            characterId,
            $"Pilot {characterId}",
            10,
            19,
            "Wing 1 / Old",
            10,
            targetSquadId,
            $"Wing 1 / Squad {targetSquadId}",
            DesiredFleetRole.SquadMember,
            "squad_member");
}

public sealed class LiveFleetSelectionModelTests
{
    [Fact]
    public void ShiftSelectIncludesRangeAndControlTogglePreservesOthers()
    {
        var model = new LiveFleetSelectionModel();
        var members = Enumerable.Range(1, 5).Select(Member).ToArray();

        model.Select(members, 2, extendRange: false, toggle: false);
        model.Select(members, 4, extendRange: true, toggle: false);

        Assert.Equal([2L, 3L, 4L], SelectedIds(members));

        model.Select(members, 5, extendRange: false, toggle: true);
        Assert.Equal([2L, 3L, 4L, 5L], SelectedIds(members));
        model.Select(members, 3, extendRange: false, toggle: true);
        Assert.Equal([2L, 4L, 5L], SelectedIds(members));
    }

    [Fact]
    public void SelectAllVisibleSkipsFleetBossAndFilteredMembers()
    {
        var ordinary = Member(1);
        var hidden = Member(2);
        hidden.IsVisible = false;
        var boss = new LiveFleetBoardMemberViewModel(
            3, "Boss", "fleet_commander", "Fleet Commander", "Monitor", -1, -1,
            "Fleet Command", "Command", null);

        LiveFleetSelectionModel.SelectAllVisible([ordinary, hidden, boss]);

        Assert.True(ordinary.IsSelected);
        Assert.False(hidden.IsSelected);
        Assert.False(boss.IsSelected);
    }

    private static long[] SelectedIds(IEnumerable<LiveFleetBoardMemberViewModel> members) =>
        members.Where(member => member.IsSelected).Select(member => member.CharacterId).ToArray();

    private static LiveFleetBoardMemberViewModel Member(int id) =>
        new(id, $"Pilot {id}", "squad_member", "Squad Member", "Scimitar", 10, 20,
            "Wing 1", "Squad 1", null);
}

public sealed class LiveFleetTargetPolicyTests
{
    private static readonly LiveFleetSquadTargetViewModel[] Targets =
    [
        new(10, 0, "Wing 1 — Wing Commander", DesiredFleetRole.WingCommander),
        new(10, 20, "Wing 1 / Squad 1 — Squad Commander", DesiredFleetRole.SquadCommander),
        new(10, 20, "Wing 1 / Squad 1 — Squad members", DesiredFleetRole.SquadMember),
    ];

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 1)]
    [InlineData(10, 1)]
    public void CommanderSeatsAppearOnlyForExactlyOnePilot(int pilotCount, int expectedCount)
    {
        Assert.Equal(expectedCount, LiveFleetTargetPolicy.ForPilotCount(Targets, pilotCount).Length);
    }

    [Fact]
    public void OccupiedCommandSeatsAreFiltered()
    {
        var targets = LiveFleetTargetPolicy.ForPilotCount(
            Targets,
            1,
            target => target.DesiredRole != DesiredFleetRole.WingCommander);

        Assert.DoesNotContain(targets, target => target.DesiredRole == DesiredFleetRole.WingCommander);
        Assert.Contains(targets, target => target.DesiredRole == DesiredFleetRole.SquadCommander);
    }
}

public sealed class LiveFleetRunCoordinatorTests
{
    [Fact]
    public async Task PreparePersistsOnlyTheInternalWorkingProfileAndBuildsApplyPlan()
    {
        var repository = new RecordingProfileRepository();
        var coordinator = new LiveFleetRunCoordinator(repository);
        var move = new StagedLiveMoveViewModel(
            9002,
            "Logi Pilot",
            10,
            20,
            "Wing 1 / Squad 1",
            10,
            21,
            "Wing 1 / Squad 2",
            DesiredFleetRole.SquadMember,
            "squad_member");

        var result = await coordinator.PrepareAsync(Snapshot(), [move], [], []);

        Assert.Equal(1, result.RequestedChangeCount);
        Assert.Equal(FleetRunMode.ApplyLiveChanges, result.Plan.Mode);
        Assert.Same(result.Profile, repository.InternalProfile);
        Assert.Null(repository.PublicProfile);
    }

    [Fact]
    public async Task PrepareRejectsAnEmptyQueueBeforePersistence()
    {
        var repository = new RecordingProfileRepository();
        var coordinator = new LiveFleetRunCoordinator(repository);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.PrepareAsync(Snapshot(), [], [], []));

        Assert.Contains("Stage at least one", error.Message, StringComparison.Ordinal);
        Assert.Null(repository.InternalProfile);
    }

    private static LiveFleetSnapshot Snapshot() =>
        new(
            7001,
            9001,
            "Fleet Boss",
            true,
            false,
            false,
            false,
            string.Empty,
            DateTimeOffset.UtcNow,
            null,
            [new LiveFleetWing(10, "Wing 1", [
                new LiveFleetSquad(20, "Squad 1"),
                new LiveFleetSquad(21, "Squad 2")])],
            [
                Member(9001, "Fleet Boss", "fleet_commander", -1, -1),
                Member(9002, "Logi Pilot", "squad_member", 10, 20),
            ]);

    private static LiveFleetMember Member(
        long id,
        string name,
        string role,
        long wingId,
        long squadId) =>
        new(id, name, role, role.Replace('_', ' '), 1, "Scimitar", 30000142, "Jita",
            null, null, wingId, squadId, DateTimeOffset.UtcNow, true);

    private sealed class RecordingProfileRepository : IFleetProfileRepository
    {
        public FleetProfile? InternalProfile { get; private set; }

        public FleetProfile? PublicProfile { get; private set; }

        public Task<FleetProfile[]> LoadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<FleetProfile>());

        public Task SaveAsync(FleetProfile profile, CancellationToken cancellationToken = default)
        {
            PublicProfile = profile;
            return Task.CompletedTask;
        }

        public Task SaveInternalAsync(FleetProfile profile, CancellationToken cancellationToken = default)
        {
            InternalProfile = profile;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

public sealed class LiveFleetWorkflowPolicyTests
{
    [Theory]
    [InlineData(false, true, false, LiveFleetApplyReadiness.NoQueuedChanges)]
    [InlineData(true, false, false, LiveFleetApplyReadiness.NoQueuedChanges)]
    [InlineData(true, true, true, LiveFleetApplyReadiness.Busy)]
    [InlineData(true, true, false, LiveFleetApplyReadiness.Ready)]
    public void ApplyReadinessExplainsWhyTheButtonCannotProceed(
        bool hasSnapshot,
        bool hasChanges,
        bool isBusy,
        LiveFleetApplyReadiness expected)
    {
        Assert.Equal(
            expected,
            LiveFleetApplyPolicy.CheckBeforePreparation(hasSnapshot, hasChanges, isBusy));
    }

    [Fact]
    public void PreparedRunBlockersPreferActiveOperationThenBusyThenPlanDetail()
    {
        Assert.Contains(
            "previous fleet run",
            LiveFleetApplyPolicy.GetPreparedRunBlocker(false, true, true, "plan", "status")!,
            StringComparison.Ordinal);
        Assert.Contains(
            "finishing another saved operation",
            LiveFleetApplyPolicy.GetPreparedRunBlocker(false, false, true, "plan", "status")!,
            StringComparison.Ordinal);
        Assert.Equal(
            "plan",
            LiveFleetApplyPolicy.GetPreparedRunBlocker(false, false, false, "plan", "status"));
        Assert.Null(LiveFleetApplyPolicy.GetPreparedRunBlocker(true, true, true, "plan", "status"));
    }

    [Fact]
    public void RefreshReconcilesAcceptedDepartedAndCompletedWork()
    {
        var pending = new LiveFleetPendingChanges();
        pending.AddInvite(new StagedLiveInviteViewModel(
            9003, "Joined", 10, 20, "Wing 1 / Squad 1", DesiredFleetRole.SquadMember));
        pending.AddMove(Move(9002, targetSquadId: 21));
        pending.AddMove(Move(9999, targetSquadId: 21));
        var snapshot = Snapshot(
            Member(9002, "Moved", "squad_member", 10, 21),
            Member(9003, "Joined", "squad_member", 10, 20));

        var result = LiveFleetReconciliation.Apply(pending, snapshot);

        Assert.Equal(new LiveFleetReconciliationResult(1, 1, 1), result);
        Assert.Empty(pending.Invites);
        Assert.Empty(pending.Moves);
    }

    [Fact]
    public void DestructiveOccupancyUsesActualEvePositionNotStagedDestination()
    {
        var snapshot = Snapshot(Member(9002, "Still Here", "squad_member", 10, 20));
        var stagedOut = Move(9002, targetSquadId: 21);

        Assert.Equal(21, stagedOut.TargetSquadId);
        Assert.False(LiveFleetOccupancyPolicy.IsSquadActuallyEmpty(snapshot, 20));
        Assert.False(LiveFleetOccupancyPolicy.IsWingActuallyEmpty(snapshot, 10));
        Assert.True(LiveFleetOccupancyPolicy.IsSquadActuallyEmpty(snapshot, 21));
    }

    private static StagedLiveMoveViewModel Move(long characterId, long targetSquadId) =>
        new(characterId, $"Pilot {characterId}", 10, 20, "Wing 1 / Squad 1", 10,
            targetSquadId, $"Wing 1 / Squad {targetSquadId}", DesiredFleetRole.SquadMember,
            "squad_member");

    private static LiveFleetSnapshot Snapshot(params LiveFleetMember[] members) =>
        new(7001, 9001, "Fleet Boss", true, false, false, false, string.Empty,
            DateTimeOffset.UtcNow, null,
            [new LiveFleetWing(10, "Wing 1", [
                new LiveFleetSquad(20, "Squad 1"),
                new LiveFleetSquad(21, "Squad 2")])],
            members);

    private static LiveFleetMember Member(
        long id,
        string name,
        string role,
        long wingId,
        long squadId) =>
        new(id, name, role, role.Replace('_', ' '), 1, "Scimitar", 30000142, "Jita",
            null, null, wingId, squadId, DateTimeOffset.UtcNow, true);
}

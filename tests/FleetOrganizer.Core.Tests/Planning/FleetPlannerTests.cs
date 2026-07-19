using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Planning;

namespace FleetOrganizer.Core.Tests.Planning;

public sealed class FleetPlannerTests
{
    [Fact]
    public void MatchingFleetProducesNoChangesAndLeavesExtraMembersUntouched()
    {
        var squadId = Guid.NewGuid();
        var profileSquads = new[] { new ProfileSquad(squadId, "Squad 1", 0) };
        var profileWings = new[]
        {
            new ProfileWing(Guid.NewGuid(), "Wing 1", 0, profileSquads),
        };
        var assignments = new[]
        {
            new ProfileAssignment(9001, "Fleet Boss", squadId, DesiredFleetRole.FleetCommander),
            new ProfileAssignment(9002, "Line Pilot", squadId, DesiredFleetRole.SquadMember),
        };
        var profile = new FleetProfile(Guid.NewGuid(), "Matching", profileWings, assignments);
        var liveSquads = new[] { new LiveFleetSquad(20, "Squad 1") };
        var liveWings = new[] { new LiveFleetWing(10, "Wing 1", liveSquads) };
        var members = new[]
        {
            CreateMember(9001, "Fleet Boss", "fleet_commander", "Fleet Commander", -1, -1),
            CreateMember(9002, "Line Pilot", "squad_member", "Squad Member", 10, 20),
            CreateMember(9003, "Unmanaged Guest", "squad_member", "Squad Member", 10, 20),
        };
        var snapshot = CreateSnapshot(liveWings, members);

        var plan = FleetPlanner.Build(profile, snapshot);

        Assert.True(plan.CanExecute);
        Assert.Equal(0, plan.TotalChanges);
        Assert.Equal(2, plan.AlreadyCorrect);
        Assert.Equal(1, plan.IgnoredLiveMembers);
    }

    [Fact]
    public void MissingStructureAndCharacterAreOrderedAsCreateThenInvite()
    {
        var squadId = Guid.NewGuid();
        var profileSquads = new[] { new ProfileSquad(squadId, "Logi", 0) };
        var profileWings = new[]
        {
            new ProfileWing(Guid.NewGuid(), "Support", 0, profileSquads),
        };
        var assignments = new[]
        {
            new ProfileAssignment(9002, "Logi Pilot", squadId, DesiredFleetRole.SquadMember),
        };
        var profile = new FleetProfile(Guid.NewGuid(), "Missing", profileWings, assignments);
        var liveWings = Array.Empty<LiveFleetWing>();
        var members = new[]
        {
            CreateMember(9001, "Fleet Boss", "fleet_commander", "Fleet Commander", -1, -1),
        };
        var snapshot = CreateSnapshot(liveWings, members);

        var plan = FleetPlanner.Build(profile, snapshot);

        Assert.True(plan.CanExecute);
        Assert.Equal(2, plan.StructureChanges);
        Assert.Equal(1, plan.CharacterInvites);
        Assert.Collection(
            plan.Items,
            item => Assert.Equal(FleetPlanItemKind.CreateWing, item.Kind),
            item => Assert.Equal(FleetPlanItemKind.CreateSquad, item.Kind),
            item => Assert.Equal(FleetPlanItemKind.InviteCharacter, item.Kind));
    }

    [Fact]
    public void MisplacedMemberWithWrongRoleProducesMoveAndRoleChange()
    {
        var squadId = Guid.NewGuid();
        var profileSquads = new[] { new ProfileSquad(squadId, "Alpha", 0) };
        var profileWings = new[]
        {
            new ProfileWing(Guid.NewGuid(), "Main", 0, profileSquads),
        };
        var assignments = new[]
        {
            new ProfileAssignment(9002, "Commander", squadId, DesiredFleetRole.SquadCommander),
        };
        var profile = new FleetProfile(Guid.NewGuid(), "Move", profileWings, assignments);
        var targetSquads = new[] { new LiveFleetSquad(20, "Alpha") };
        var otherSquads = new[] { new LiveFleetSquad(40, "Other") };
        var liveWings = new[]
        {
            new LiveFleetWing(10, "Main", targetSquads),
            new LiveFleetWing(30, "Spare", otherSquads),
        };
        var members = new[]
        {
            CreateMember(9001, "Fleet Boss", "fleet_commander", "Fleet Commander", -1, -1),
            CreateMember(9002, "Commander", "squad_member", "Squad Member", 30, 40),
        };
        var snapshot = CreateSnapshot(liveWings, members);

        var plan = FleetPlanner.Build(profile, snapshot);

        Assert.Equal(1, plan.CharacterMoves);
        Assert.Equal(1, plan.RoleChanges);
        Assert.Equal(2, plan.TotalChanges);
        Assert.Contains(plan.Items, item =>
            item.Kind == FleetPlanItemKind.MoveCharacter &&
            item.Detail.Contains("Spare / Other", StringComparison.Ordinal));
    }

    [Fact]
    public void FleetBossTransferIsABlockingIssueAndNeverAnInvite()
    {
        var squadId = Guid.NewGuid();
        var profileSquads = new[] { new ProfileSquad(squadId, "Squad 1", 0) };
        var profileWings = new[]
        {
            new ProfileWing(Guid.NewGuid(), "Wing 1", 0, profileSquads),
        };
        var assignments = new[]
        {
            new ProfileAssignment(9002, "Other FC", squadId, DesiredFleetRole.FleetCommander),
        };
        var profile = new FleetProfile(Guid.NewGuid(), "Unsafe", profileWings, assignments);
        var liveSquads = new[] { new LiveFleetSquad(20, "Squad 1") };
        var liveWings = new[] { new LiveFleetWing(10, "Wing 1", liveSquads) };
        var members = new[]
        {
            CreateMember(9001, "Fleet Boss", "fleet_commander", "Fleet Commander", -1, -1),
        };
        var snapshot = CreateSnapshot(liveWings, members);

        var plan = FleetPlanner.Build(profile, snapshot);

        Assert.False(plan.CanExecute);
        Assert.Equal(1, plan.BlockingIssues);
        Assert.Equal(0, plan.CharacterInvites);
        Assert.Contains("transfer", Assert.Single(plan.Items).Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FleetBossMayRemainWingCommanderWhileInvitingAnotherCharacter()
    {
        var squadId = Guid.NewGuid();
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Boss is wing commander",
            [
                new ProfileWing(
                    Guid.NewGuid(),
                    "Wing 1",
                    0,
                    [new ProfileSquad(squadId, "Squad 1", 0)]),
            ],
            [
                new ProfileAssignment(9001, "Fleet Boss", squadId, DesiredFleetRole.WingCommander),
                new ProfileAssignment(9002, "Invited Pilot", squadId, DesiredFleetRole.SquadMember),
            ]);
        var snapshot = CreateSnapshot(
            [new LiveFleetWing(10, "Wing 1", [new LiveFleetSquad(20, "Squad 1")])],
            [CreateMember(9001, "Fleet Boss", "wing_commander", "Wing Commander", 10, -1)]);

        var plan = FleetPlanner.Build(profile, snapshot);

        Assert.True(plan.CanExecute);
        Assert.Equal(0, plan.BlockingIssues);
        Assert.Equal(1, plan.CharacterInvites);
        Assert.Contains(plan.Items, item =>
            item.Kind == FleetPlanItemKind.AlreadyCorrect &&
            item.Title.Contains("Wing Commander", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AmbiguousLiveHierarchyBlocksTargetPlacement()
    {
        var squadId = Guid.NewGuid();
        var profileSquads = new[] { new ProfileSquad(squadId, "Alpha", 0) };
        var profileWings = new[]
        {
            new ProfileWing(Guid.NewGuid(), "Main", 0, profileSquads),
        };
        var assignments = new[]
        {
            new ProfileAssignment(9002, "Line Pilot", squadId, DesiredFleetRole.SquadMember),
        };
        var profile = new FleetProfile(Guid.NewGuid(), "Ambiguous", profileWings, assignments);
        var firstSquads = new[] { new LiveFleetSquad(20, "Alpha") };
        var secondSquads = new[] { new LiveFleetSquad(40, "Alpha") };
        var liveWings = new[]
        {
            new LiveFleetWing(10, "Main", firstSquads),
            new LiveFleetWing(30, "main", secondSquads),
        };
        var members = new[]
        {
            CreateMember(9001, "Fleet Boss", "fleet_commander", "Fleet Commander", -1, -1),
            CreateMember(9002, "Line Pilot", "squad_member", "Squad Member", 10, 20),
        };
        var snapshot = CreateSnapshot(liveWings, members);

        var plan = FleetPlanner.Build(profile, snapshot);

        Assert.False(plan.CanExecute);
        Assert.Equal(2, plan.BlockingIssues);
        Assert.Contains(plan.Items, item =>
            item.Kind == FleetPlanItemKind.Blocked &&
            item.Title.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EmptyOrderedSquadCanBeRenamedInsteadOfCreatingADuplicate()
    {
        var squadId = Guid.NewGuid();
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Repair",
            [
                new ProfileWing(
                    Guid.NewGuid(),
                    "Wing 2",
                    0,
                    [new ProfileSquad(squadId, "Squad 1", 0)]),
            ],
            [new ProfileAssignment(9002, "Line Pilot", squadId, DesiredFleetRole.SquadMember)]);
        var snapshot = CreateSnapshot(
            [new LiveFleetWing(10, "Wing 2", [new LiveFleetSquad(20, "Squad 2")])],
            [CreateMember(9001, "Fleet Boss", "fleet_commander", "Fleet Commander", -1, -1)]);

        var plan = FleetPlanner.Build(profile, snapshot);

        var rename = Assert.Single(
            plan.Items,
            item => item.Kind == FleetPlanItemKind.RenameSquad);
        Assert.Equal(20, rename.LiveSquadId);
        Assert.Equal("Squad 1", rename.TargetSquadName);
        Assert.DoesNotContain(plan.Items, item => item.Kind == FleetPlanItemKind.CreateSquad);
    }

    [Fact]
    public void UnmanagedCommanderInDesiredSlotBlocksPromotion()
    {
        var squadId = Guid.NewGuid();
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Commander safety",
            [
                new ProfileWing(
                    Guid.NewGuid(),
                    "Main",
                    0,
                    [new ProfileSquad(squadId, "Alpha", 0)]),
            ],
            [new ProfileAssignment(9002, "New Commander", squadId, DesiredFleetRole.SquadCommander)]);
        var snapshot = CreateSnapshot(
            [new LiveFleetWing(10, "Main", [new LiveFleetSquad(20, "Alpha")])],
            [
                CreateMember(9001, "Fleet Boss", "fleet_commander", "Fleet Commander", -1, -1),
                CreateMember(9003, "Unmanaged Commander", "squad_commander", "Squad Commander", 10, 20),
            ]);

        var plan = FleetPlanner.Build(profile, snapshot);

        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Items, item =>
            item.Kind == FleetPlanItemKind.Blocked &&
            item.Detail.Contains("Unmanaged Commander", StringComparison.Ordinal));
    }

    private static LiveFleetSnapshot CreateSnapshot(
        LiveFleetWing[] wings,
        LiveFleetMember[] members) =>
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
            wings,
            members);

    private static LiveFleetMember CreateMember(
        long characterId,
        string characterName,
        string role,
        string roleName,
        long wingId,
        long squadId) =>
        new(
            characterId,
            characterName,
            role,
            roleName,
            587,
            "Rifter",
            30_000_142,
            "Jita",
            null,
            null,
            wingId,
            squadId,
            DateTimeOffset.UtcNow,
            true);
}

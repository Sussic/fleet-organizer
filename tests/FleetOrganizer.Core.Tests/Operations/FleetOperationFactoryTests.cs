using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;
using FleetOrganizer.Core.Planning;

namespace FleetOrganizer.Core.Tests.Operations;

public sealed class FleetOperationFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MissingOrdinaryMemberCreatesDurableInviteAndWaitingPlacement()
    {
        var profile = CreateProfile(DesiredFleetRole.SquadMember);
        var snapshot = CreateSnapshot(includePilot: false);
        var plan = FleetPlanner.Build(profile, snapshot);

        var operation = FleetOperationFactory.Create(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            profile,
            snapshot,
            plan,
            Now);

        Assert.Collection(
            operation.Steps,
            step =>
            {
                Assert.Equal(FleetOperationStepType.Invite, step.Type);
                Assert.Equal(FleetOperationStepState.Pending, step.State);
                Assert.Equal(9002, step.Target.CharacterId);
            },
            step =>
            {
                Assert.Equal(FleetOperationStepType.Place, step.Type);
                Assert.Equal(FleetOperationStepState.Waiting, step.State);
                Assert.Equal(20, step.Target.SquadId);
            });
    }

    [Fact]
    public void ExistingOrdinaryMemberInWrongSquadCreatesPlacementOnly()
    {
        var profile = CreateProfile(DesiredFleetRole.SquadMember);
        var snapshot = CreateSnapshot(includePilot: true, pilotSquadId: 21);
        var plan = FleetPlanner.Build(profile, snapshot);

        var operation = FleetOperationFactory.Create(
            Guid.NewGuid(),
            profile,
            snapshot,
            plan,
            Now);

        var step = Assert.Single(operation.Steps);
        Assert.Equal(FleetOperationStepType.Place, step.Type);
        Assert.Equal(FleetOperationStepState.Pending, step.State);
    }

    [Fact]
    public void MissingCommanderIsInvitedStagedAndPromotedInOrder()
    {
        var profile = CreateProfile(DesiredFleetRole.SquadCommander);
        var snapshot = CreateSnapshot(includePilot: false);
        var plan = FleetPlanner.Build(profile, snapshot);

        var operation = FleetOperationFactory.Create(
            Guid.NewGuid(),
            profile,
            snapshot,
            plan,
            Now);

        Assert.Collection(
            operation.Steps,
            step => Assert.Equal(FleetOperationStepType.Invite, step.Type),
            step =>
            {
                Assert.Equal(FleetOperationStepType.Place, step.Type);
                Assert.Equal(FleetOperationStepState.Waiting, step.State);
            },
            step =>
            {
                Assert.Equal(FleetOperationStepType.PromoteCommander, step.Type);
                Assert.Equal(FleetOperationStepState.Waiting, step.State);
            });
    }

    [Fact]
    public void MissingStructureCreatesDurableCreateAndRenameSteps()
    {
        var profile = CreateProfile(DesiredFleetRole.SquadMember);
        var snapshot = CreateSnapshot(includePilot: false) with { Wings = [] };
        var plan = FleetPlanner.Build(profile, snapshot);

        var operation = FleetOperationFactory.Create(
            Guid.NewGuid(),
            profile,
            snapshot,
            plan,
            Now);

        Assert.Equal(OperationState.EnsureStructure, operation.State);
        Assert.Collection(
            operation.Steps.Take(4),
            step => Assert.Equal(FleetOperationStepType.CreateWing, step.Type),
            step => Assert.Equal(FleetOperationStepType.RenameWing, step.Type),
            step => Assert.Equal(FleetOperationStepType.CreateSquad, step.Type),
            step => Assert.Equal(FleetOperationStepType.RenameSquad, step.Type));
    }

    private static FleetProfile CreateProfile(DesiredFleetRole role)
    {
        var squadId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        return new FleetProfile(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Test Fleet",
            [
                new ProfileWing(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "Main Wing",
                    0,
                    [new ProfileSquad(squadId, "Squad 1", 0)]),
            ],
            [new ProfileAssignment(9002, "Second Pilot", squadId, role)]);
    }

    private static LiveFleetSnapshot CreateSnapshot(
        bool includePilot,
        long pilotSquadId = 20)
    {
        var members = new List<LiveFleetMember>
        {
            CreateMember(9001, "Fleet Boss", "fleet_commander", -1, -1),
        };
        if (includePilot)
        {
            members.Add(CreateMember(
                9002,
                "Second Pilot",
                "squad_member",
                10,
                pilotSquadId));
        }

        return new LiveFleetSnapshot(
            7001,
            9001,
            "Fleet Boss",
            IsFleetBoss: true,
            IsFreeMove: false,
            IsRegistered: false,
            IsVoiceEnabled: false,
            Motd: string.Empty,
            Now,
            Now.AddSeconds(5),
            [
                new LiveFleetWing(
                    10,
                    "Main Wing",
                    [
                        new LiveFleetSquad(20, "Squad 1"),
                        new LiveFleetSquad(21, "Spare"),
                    ]),
            ],
            members.ToArray());
    }

    private static LiveFleetMember CreateMember(
        long characterId,
        string name,
        string role,
        long wingId,
        long squadId) =>
        new(
            characterId,
            name,
            role,
            role.Replace('_', ' '),
            587,
            "Rifter",
            30000142,
            "Jita",
            null,
            null,
            wingId,
            squadId,
            Now,
            TakesFleetWarp: true);
}

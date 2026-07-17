using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.Core.Tests.Profiles;

public sealed class ShipRuleResolverTests
{
    [Fact]
    public void MatchingLiveShipAddsAnOrdinarySquadMemberAssignment()
    {
        var squadId = Guid.NewGuid();
        var profile = CreateProfile(squadId);
        var snapshot = CreateSnapshot(
            CreateMember(9001, "Fleet Boss", "Orca", "fleet_commander"),
            CreateMember(9002, "Miner One", "Hulk", "squad_member"));

        var result = ShipRuleResolver.Resolve(profile, snapshot);

        var assignment = Assert.Single(
            result.EffectiveProfile.Assignments,
            item => item.CharacterId == 9002);
        Assert.Equal(squadId, assignment.TargetSquadId);
        Assert.Equal(DesiredFleetRole.SquadMember, assignment.DesiredRole);
        Assert.Single(result.Matches);
    }

    [Fact]
    public void ExplicitCharacterAssignmentWinsOverShipRule()
    {
        var ruleSquadId = Guid.NewGuid();
        var explicitSquadId = Guid.NewGuid();
        var profile = CreateProfile(ruleSquadId, explicitSquadId) with
        {
            Assignments =
            [
                new(9002, "Miner One", explicitSquadId, DesiredFleetRole.SquadCommander),
            ],
        };
        var snapshot = CreateSnapshot(
            CreateMember(9001, "Fleet Boss", "Orca", "fleet_commander"),
            CreateMember(9002, "Miner One", "Hulk", "squad_member"));

        var result = ShipRuleResolver.Resolve(profile, snapshot);

        var assignment = Assert.Single(result.EffectiveProfile.Assignments);
        Assert.Equal(explicitSquadId, assignment.TargetSquadId);
        Assert.Equal(DesiredFleetRole.SquadCommander, assignment.DesiredRole);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void FleetBossAndUnmatchedShipsAreLeftUntouched()
    {
        var squadId = Guid.NewGuid();
        var profile = CreateProfile(squadId);
        var snapshot = CreateSnapshot(
            CreateMember(9001, "Fleet Boss", "Hulk", "fleet_commander"),
            CreateMember(9002, "Hauler", "Miasmos", "squad_member"));

        var result = ShipRuleResolver.Resolve(profile, snapshot);

        Assert.Empty(result.EffectiveProfile.Assignments);
        Assert.Empty(result.Matches);
    }

    private static FleetProfile CreateProfile(Guid ruleSquadId, Guid? secondSquadId = null)
    {
        var squads = new List<ProfileSquad>
        {
            new(ruleSquadId, "Barges", 0),
        };
        if (secondSquadId is Guid id)
        {
            squads.Add(new(id, "Command", 1));
        }

        return new FleetProfile(
            Guid.NewGuid(),
            "Mining",
            [new(Guid.NewGuid(), "Main", 0, squads)],
            [])
        {
            ShipRules = [new(Guid.NewGuid(), "Hulk", ruleSquadId, 0)],
        };
    }

    private static LiveFleetSnapshot CreateSnapshot(params LiveFleetMember[] members) =>
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
            [new(10, "Main", [new(20, "Barges")])],
            members);

    private static LiveFleetMember CreateMember(
        long characterId,
        string characterName,
        string shipTypeName,
        string role) =>
        new(
            characterId,
            characterName,
            role,
            role.Replace('_', ' '),
            587,
            shipTypeName,
            30_000_142,
            "Jita",
            null,
            null,
            10,
            20,
            DateTimeOffset.UtcNow,
            true);
}

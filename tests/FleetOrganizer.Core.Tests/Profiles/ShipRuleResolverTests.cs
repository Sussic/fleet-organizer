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

    [Fact]
    public void MultipleExactShipsUsePriorityThenFallback()
    {
        var primarySquadId = Guid.NewGuid();
        var fallbackSquadId = Guid.NewGuid();
        var profile = CreateProfile(primarySquadId, fallbackSquadId) with
        {
            ShipRules =
            [
                new(Guid.NewGuid(), "Hulk, Mackinaw", primarySquadId, 0) { Label = "Mining hulls" },
                new(Guid.NewGuid(), string.Empty, fallbackSquadId, 1) { Label = "Other ships", IsFallback = true },
            ],
        };
        var snapshot = CreateSnapshot(
            CreateMember(9001, "Fleet Boss", "Orca", "fleet_commander"),
            CreateMember(9002, "Miner One", "Hulk", "squad_member"),
            CreateMember(9003, "Miner Two", "Mackinaw", "squad_member"),
            CreateMember(9004, "Hauler", "Miasmos", "squad_member"));

        var result = ShipRuleResolver.Resolve(profile, snapshot);

        Assert.Equal(3, result.Matches.Count);
        Assert.Equal(
            2,
            result.EffectiveProfile.Assignments.Count(assignment => assignment.TargetSquadId == primarySquadId));
        Assert.Equal(
            fallbackSquadId,
            Assert.Single(result.EffectiveProfile.Assignments, assignment => assignment.CharacterId == 9004).TargetSquadId);
        Assert.Contains(result.Matches, match => match.RuleName == "Mining hulls");
        Assert.Contains(result.Matches, match => match.RuleName == "Other ships");
    }

    [Fact]
    public void CapacityAwareBalancingUsesOverflowAndReportsUnplacedMembers()
    {
        var primarySquadId = Guid.NewGuid();
        var overflowSquadId = Guid.NewGuid();
        var profile = CreateProfile(primarySquadId, overflowSquadId) with
        {
            Assignments = [new(8001, "Reserved", primarySquadId, DesiredFleetRole.SquadMember)],
            ShipRules =
            [
                new(Guid.NewGuid(), "Hulk", primarySquadId, 0)
                {
                    Label = "Barges",
                    OverflowSquadId = overflowSquadId,
                    MaximumPerSquad = 2,
                    BalanceAcrossTargets = true,
                },
            ],
        };
        var snapshot = CreateSnapshot(
            CreateMember(9001, "Fleet Boss", "Orca", "fleet_commander"),
            CreateMember(9002, "Miner One", "Hulk", "squad_member"),
            CreateMember(9003, "Miner Two", "Hulk", "squad_member"),
            CreateMember(9004, "Miner Three", "Hulk", "squad_member"),
            CreateMember(9005, "Miner Four", "Hulk", "squad_member"));

        var result = ShipRuleResolver.Resolve(profile, snapshot);

        Assert.Equal(3, result.Matches.Count);
        Assert.Single(result.CapacitySkipped);
        Assert.Equal("Barges", Assert.Single(result.CapacitySkipped).RuleName);
        Assert.Equal(
            2,
            result.EffectiveProfile.Assignments.Count(assignment => assignment.TargetSquadId == primarySquadId));
        Assert.Equal(
            2,
            result.EffectiveProfile.Assignments.Count(assignment => assignment.TargetSquadId == overflowSquadId));
    }

    [Fact]
    public void TwoHundredMemberResolutionIsDeterministicAndCapacitySafe()
    {
        var squadIds = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();
        var wings = Enumerable.Range(0, 4)
            .Select(wingIndex => new ProfileWing(
                Guid.NewGuid(),
                $"Wing {wingIndex}",
                wingIndex,
                Enumerable.Range(0, 5)
                    .Select(squadIndex =>
                    {
                        var index = (wingIndex * 5) + squadIndex;
                        return new ProfileSquad(squadIds[index], $"S{index}", squadIndex);
                    })
                    .ToArray()))
            .ToArray();
        var rules = Enumerable.Range(0, 20)
            .Select(index => new ProfileShipRule(
                Guid.NewGuid(),
                $"Doctrine {index}",
                squadIds[index],
                index)
            {
                MaximumPerSquad = 10,
            })
            .ToArray();
        var profile = new FleetProfile(Guid.NewGuid(), "Scale", wings, [])
        {
            ShipRules = rules,
        };
        var members = new List<LiveFleetMember>
        {
            CreateMember(9001, "Fleet Boss", "Command", "fleet_commander"),
        };
        for (var shipIndex = 0; shipIndex < 20; shipIndex++)
        {
            for (var pilotIndex = 0; pilotIndex < 10; pilotIndex++)
            {
                var characterId = 10_000L + (shipIndex * 10L) + pilotIndex;
                members.Add(CreateMember(
                    characterId,
                    $"Pilot {shipIndex:D2}-{pilotIndex:D2}",
                    $"Doctrine {shipIndex}",
                    "squad_member"));
            }
        }

        var first = ShipRuleResolver.Resolve(profile, CreateSnapshot(members.ToArray()));
        var second = ShipRuleResolver.Resolve(profile, CreateSnapshot(members.ToArray()));

        Assert.Equal(200, first.Matches.Count);
        Assert.Empty(first.CapacitySkipped);
        Assert.All(
            first.EffectiveProfile.Assignments.GroupBy(assignment => assignment.TargetSquadId),
            group => Assert.Equal(10, group.Count()));
        Assert.Equal(
            first.EffectiveProfile.Assignments.Select(assignment => (assignment.CharacterId, assignment.TargetSquadId)),
            second.EffectiveProfile.Assignments.Select(assignment => (assignment.CharacterId, assignment.TargetSquadId)));
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

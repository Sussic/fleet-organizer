using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.Core.Tests.Profiles;

public sealed class FleetProfileFactoryTests
{
    [Fact]
    public void FromLiveFleetMapsHierarchyCharactersAndDesiredRoles()
    {
        var squads = new[] { new LiveFleetSquad(20, "Squad 1") };
        var wings = new[] { new LiveFleetWing(10, "Main Wing", squads) };
        var members = new[]
        {
            CreateMember(9001, "Fleet Boss", "fleet_commander", -1, -1),
            CreateMember(9002, "Squad Boss", "squad_commander", 10, 20),
        };
        var snapshot = new LiveFleetSnapshot(
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

        var profile = FleetProfileFactory.FromLiveFleet(snapshot, "Captured Fleet");

        Assert.Equal("Captured Fleet", profile.Name);
        var wing = Assert.Single(profile.Wings);
        var squad = Assert.Single(wing.Squads);
        Assert.Equal("Main Wing", wing.Name);
        Assert.Equal("Squad 1", squad.Name);
        Assert.Collection(
            profile.Assignments,
            assignment =>
            {
                Assert.Equal(9001, assignment.CharacterId);
                Assert.Equal(DesiredFleetRole.FleetCommander, assignment.DesiredRole);
                Assert.Equal(squad.Id, assignment.TargetSquadId);
            },
            assignment =>
            {
                Assert.Equal(9002, assignment.CharacterId);
                Assert.Equal(DesiredFleetRole.SquadCommander, assignment.DesiredRole);
                Assert.Equal(squad.Id, assignment.TargetSquadId);
            });
    }

    [Fact]
    public void DuplicateCreatesIndependentIdsAndPreservesRosterTags()
    {
        var squadId = Guid.NewGuid();
        var squads = new[] { new ProfileSquad(squadId, "Squad 1", 0) };
        var wings = new[] { new ProfileWing(Guid.NewGuid(), "Wing 1", 0, squads) };
        var tags = new[] { "logi", "anchor" };
        var assignments = new[]
        {
            new ProfileAssignment(
                9001,
                "Logi Pilot",
                squadId,
                DesiredFleetRole.SquadMember)
            {
                Tags = tags,
            },
        };
        var source = new FleetProfile(Guid.NewGuid(), "Source", wings, assignments)
        {
            ShipRules = [new(Guid.NewGuid(), "Basilisk", squadId, 0)],
        };

        var duplicate = FleetProfileFactory.Duplicate(source, "Source Copy");

        Assert.NotEqual(source.Id, duplicate.Id);
        Assert.NotEqual(source.Wings[0].Id, duplicate.Wings[0].Id);
        Assert.NotEqual(source.Wings[0].Squads[0].Id, duplicate.Wings[0].Squads[0].Id);
        var assignment = Assert.Single(duplicate.Assignments);
        Assert.Equal(duplicate.Wings[0].Squads[0].Id, assignment.TargetSquadId);
        Assert.Collection(
            assignment.Tags,
            tag => Assert.Equal("logi", tag),
            tag => Assert.Equal("anchor", tag));
        var shipRule = Assert.Single(duplicate.ShipRules);
        Assert.Equal("Basilisk", shipRule.ShipTypeName);
        Assert.Equal(duplicate.Wings[0].Squads[0].Id, shipRule.TargetSquadId);
        Assert.NotEqual(source.ShipRules[0].Id, shipRule.Id);
    }

    private static LiveFleetMember CreateMember(
        long characterId,
        string characterName,
        string role,
        long wingId,
        long squadId) =>
        new(
            characterId,
            characterName,
            role,
            role.Replace('_', ' '),
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

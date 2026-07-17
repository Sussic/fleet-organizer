using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.Core.Tests.Profiles;

public sealed class FleetProfileJsonSerializerTests
{
    [Fact]
    public void ExportRoundTripPreservesHierarchyAssignmentsRolesAndTags()
    {
        var squadId = Guid.NewGuid();
        var squads = new[] { new ProfileSquad(squadId, "Squad 1", 0) };
        var wings = new[] { new ProfileWing(Guid.NewGuid(), "Wing 1", 0, squads) };
        var tags = new[] { "logi" };
        var assignments = new[]
        {
            new ProfileAssignment(
                9001,
                "Logi Pilot",
                squadId,
                DesiredFleetRole.SquadCommander)
            {
                Tags = tags,
            },
        };
        var source = new FleetProfile(Guid.NewGuid(), "Doctrine Alpha", wings, assignments)
        {
            ShipRules = [new(Guid.NewGuid(), "Basilisk", squadId, 0)],
        };

        var json = FleetProfileJsonSerializer.Serialize(source);
        var restored = FleetProfileJsonSerializer.Deserialize(json);

        Assert.Contains("\"exportVersion\": 1", json, StringComparison.Ordinal);
        Assert.Equal(source.Id, restored.Id);
        Assert.Equal(source.Name, restored.Name);
        Assert.Equal("Wing 1", Assert.Single(restored.Wings).Name);
        var assignment = Assert.Single(restored.Assignments);
        Assert.Equal(DesiredFleetRole.SquadCommander, assignment.DesiredRole);
        Assert.Equal("logi", Assert.Single(assignment.Tags));
        var shipRule = Assert.Single(restored.ShipRules);
        Assert.Equal("Basilisk", shipRule.ShipTypeName);
        Assert.Equal(squadId, shipRule.TargetSquadId);
    }

    [Fact]
    public void OlderVersionOneProfileWithoutShipRulesStillImports()
    {
        var profileId = Guid.NewGuid();
        var json = $$"""
            {
              "exportVersion": 1,
              "profile": {
                "id": "{{profileId}}",
                "name": "Legacy",
                "wings": [],
                "assignments": []
              }
            }
            """;

        var restored = FleetProfileJsonSerializer.Deserialize(json);

        Assert.Equal(profileId, restored.Id);
        Assert.Empty(restored.ShipRules);
    }

    [Fact]
    public void ImportRejectsUnsupportedExportVersion()
    {
        const string Json = """
            {"exportVersion":99,"profile":null}
            """;

        var exception = Assert.Throws<InvalidOperationException>(
            () => FleetProfileJsonSerializer.Deserialize(Json));

        Assert.Contains("not supported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

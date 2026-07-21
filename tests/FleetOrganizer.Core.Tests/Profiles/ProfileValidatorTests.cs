using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.Core.Tests.Profiles;

public sealed class ProfileValidatorTests
{
    [Fact]
    public void EmptyProfileNameIsRejected()
    {
        var profile = FleetProfile.Create("   ");

        var errors = ProfileValidator.Validate(profile);

        Assert.Contains(errors, error => error.Code == "profile.name.required");
    }

    [Fact]
    public void DuplicateWingNamesAreRejectedCaseInsensitively()
    {
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Mining",
            [
                new(Guid.NewGuid(), "Boost", 0, []),
                new(Guid.NewGuid(), "boost", 1, []),
            ],
            []);

        var errors = ProfileValidator.Validate(profile);

        Assert.Contains(errors, error => error.Code == "wing.name.duplicate");
    }

    [Fact]
    public void HierarchyNamesLongerThanEsiLimitAreRejected()
    {
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Home",
            [new(Guid.NewGuid(), "ElevenChars", 0, [])],
            []);

        var errors = ProfileValidator.Validate(profile);

        Assert.Contains(errors, error => error.Code == "wing.name.too_long");
    }

    [Fact]
    public void AssignmentToMissingSquadIsRejected()
    {
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Home",
            [],
            [new(2_123_703_004L, "Coffee Manager", Guid.NewGuid(), DesiredFleetRole.SquadMember)]);

        var errors = ProfileValidator.Validate(profile);

        Assert.Contains(errors, error => error.Code == "assignment.squad.missing");
    }

    [Fact]
    public void TwoSquadCommandersInTheSameSquadAreRejected()
    {
        var squadId = Guid.NewGuid();
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Combat",
            [new(Guid.NewGuid(), "Main", 0, [new(squadId, "DPS", 0)])],
            [
                new(1, "Character One", squadId, DesiredFleetRole.SquadCommander),
                new(2, "Character Two", squadId, DesiredFleetRole.SquadCommander),
            ]);

        var errors = ProfileValidator.Validate(profile);

        Assert.Contains(errors, error => error.Code == "assignment.squad_commander.duplicate");
    }

    [Fact]
    public void ValidProfileHasNoErrors()
    {
        var squadId = Guid.NewGuid();
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Combat",
            [new(Guid.NewGuid(), "Main", 0, [new(squadId, "DPS", 0)])],
            [new(1, "Character One", squadId, DesiredFleetRole.SquadCommander)]);

        var errors = ProfileValidator.Validate(profile);

        Assert.Empty(errors);
    }

    [Fact]
    public void DuplicateShipPlacementRulesAreRejectedCaseInsensitively()
    {
        var squadId = Guid.NewGuid();
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Mining",
            [new(Guid.NewGuid(), "Main", 0, [new(squadId, "Barges", 0)])],
            [])
        {
            ShipRules =
            [
                new(Guid.NewGuid(), "Hulk", squadId, 0),
                new(Guid.NewGuid(), "hulk", squadId, 1),
            ],
        };

        var errors = ProfileValidator.Validate(profile);

        Assert.Contains(errors, error => error.Code == "ship_rule.match.duplicate");
    }

    [Fact]
    public void ShipPlacementRuleToMissingSquadIsRejected()
    {
        var profile = FleetProfile.Create("Mining") with
        {
            ShipRules = [new(Guid.NewGuid(), "Hulk", Guid.NewGuid(), 0)],
        };

        var errors = ProfileValidator.Validate(profile);

        Assert.Contains(errors, error => error.Code == "ship_rule.squad.missing");
    }

    [Fact]
    public void FallbackRuleMustBeUniqueAndLast()
    {
        var squadId = Guid.NewGuid();
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Mining",
            [new(Guid.NewGuid(), "Main", 0, [new(squadId, "Barges", 0)])],
            [])
        {
            ShipRules =
            [
                new(Guid.NewGuid(), string.Empty, squadId, 0) { IsFallback = true },
                new(Guid.NewGuid(), "Hulk", squadId, 1),
                new(Guid.NewGuid(), string.Empty, squadId, 2) { IsFallback = true },
            ],
        };

        var errors = ProfileValidator.Validate(profile);

        Assert.Contains(errors, error => error.Code == "ship_rule.fallback.order");
        Assert.Contains(errors, error => error.Code == "ship_rule.fallback.duplicate");
    }

    [Fact]
    public void ShipPolicyCapacityAndOverflowAreValidated()
    {
        var squadId = Guid.NewGuid();
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Mining",
            [new(Guid.NewGuid(), "Main", 0, [new(squadId, "Barges", 0)])],
            [])
        {
            ShipRules =
            [
                new(Guid.NewGuid(), "Hulk, Mackinaw", squadId, 0)
                {
                    MaximumPerSquad = 257,
                    OverflowSquadId = squadId,
                },
            ],
        };

        var errors = ProfileValidator.Validate(profile);

        Assert.Contains(errors, error => error.Code == "ship_rule.capacity.invalid");
        Assert.Contains(errors, error => error.Code == "ship_rule.overflow.invalid");
    }

    [Fact]
    public void MoreThanTwentyFiveWingsAreRejectedBeforePreflight()
    {
        var wings = Enumerable.Range(1, 26)
            .Select(index => new ProfileWing(Guid.NewGuid(), $"Wing {index}", index, []))
            .ToArray();
        var profile = new FleetProfile(Guid.NewGuid(), "Too wide", wings, []);

        var errors = ProfileValidator.Validate(profile);

        Assert.Contains(errors, error => error.Code == "profile.wings.capacity");
    }

    [Fact]
    public void FifteenCharactersInASquadAreAccepted()
    {
        var squadId = Guid.NewGuid();
        var assignments = Enumerable.Range(1, 15)
            .Select(index => new ProfileAssignment(
                index,
                $"Character {index}",
                squadId,
                DesiredFleetRole.SquadMember))
            .ToArray();
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Valid fleet",
            [new ProfileWing(Guid.NewGuid(), "Main", 0, [new ProfileSquad(squadId, "DPS", 0)])],
            assignments);

        var errors = ProfileValidator.Validate(profile);

        Assert.DoesNotContain(errors, error => error.Code == "assignment.squad.capacity");
    }

    [Fact]
    public void MoreThanTwoHundredFiftySixCharactersAreRejectedBeforePreflight()
    {
        var squadId = Guid.NewGuid();
        var assignments = Enumerable.Range(1, 257)
            .Select(index => new ProfileAssignment(
                index,
                $"Character {index}",
                squadId,
                DesiredFleetRole.SquadMember))
            .ToArray();
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Too full",
            [new ProfileWing(Guid.NewGuid(), "Main", 0, [new ProfileSquad(squadId, "DPS", 0)])],
            assignments);

        var errors = ProfileValidator.Validate(profile);

        Assert.Contains(errors, error => error.Code == "profile.assignments.capacity");
    }
}

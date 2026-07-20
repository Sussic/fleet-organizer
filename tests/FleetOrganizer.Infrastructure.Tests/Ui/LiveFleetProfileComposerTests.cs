using FleetOrganizer.App.ViewModels;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.Infrastructure.Tests.Ui;

public sealed class LiveFleetProfileComposerTests
{
    [Fact]
    public void ComposeAppliesQueuedMovesCreatesAndRenamesWithoutChangingSavedSetups()
    {
        var snapshot = Snapshot();
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
        var structureChanges = new[]
        {
            Change(StagedLiveStructureChangeKind.RenameWing, 10, 0, "Wing 1", "Wing 1", "Main"),
            Change(StagedLiveStructureChangeKind.RenameSquad, 10, 20, "Wing 1", "Squad 1", "Logi"),
            Change(StagedLiveStructureChangeKind.AddSquad, 10, 0, "Wing 1", string.Empty, "Reserve"),
            Change(StagedLiveStructureChangeKind.AddWing, 0, 0, string.Empty, string.Empty, "Scouts"),
        };
        var profileId = Guid.NewGuid();

        var profile = LiveFleetProfileComposer.Compose(
            profileId,
            snapshot,
            [move],
            [],
            structureChanges);

        Assert.Equal(profileId, profile.Id);
        Assert.Empty(ProfileValidator.Validate(profile));
        Assert.Collection(
            profile.Wings.OrderBy(wing => wing.SortOrder),
            main =>
            {
                Assert.Equal("Main", main.Name);
                Assert.Equal(
                    ["Logi", "Squad 2", "Reserve"],
                    main.Squads.OrderBy(squad => squad.SortOrder).Select(squad => squad.Name));
                var secondSquad = main.Squads.Single(squad => squad.Name == "Squad 2");
                Assert.Equal(
                    secondSquad.Id,
                    profile.Assignments.Single(assignment => assignment.CharacterId == 9002).TargetSquadId);
            },
            scouts =>
            {
                Assert.Equal("Scouts", scouts.Name);
                Assert.Empty(scouts.Squads);
            });
    }

    private static StagedLiveStructureChangeViewModel Change(
        StagedLiveStructureChangeKind kind,
        long wingId,
        long squadId,
        string wingName,
        string currentName,
        string newName) =>
        new(Guid.NewGuid(), kind, wingId, squadId, wingName, currentName, newName);

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
            [
                new LiveFleetWing(
                    10,
                    "Wing 1",
                    [new LiveFleetSquad(20, "Squad 1"), new LiveFleetSquad(21, "Squad 2")]),
            ],
            [
                Member(9001, "Fleet Boss", "fleet_commander", -1, -1),
                Member(9002, "Logi Pilot", "squad_member", 10, 20),
            ]);

    private static LiveFleetMember Member(
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
            1,
            "Scimitar",
            30000142,
            "Jita",
            60003760,
            "Jita IV - Moon 4",
            wingId,
            squadId,
            DateTimeOffset.UtcNow,
            true);
}

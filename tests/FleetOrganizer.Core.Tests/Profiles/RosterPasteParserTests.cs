using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.Core.Tests.Profiles;

public sealed class RosterPasteParserTests
{
    [Fact]
    public void ParseAcceptsNewLinesCommasAndTabsAndRemovesDuplicates()
    {
        var entries = RosterPasteParser.Parse(
            "Alpha Pilot, Bravo Pilot\r\nCharlie Pilot\tDelta Pilot\r\nalpha pilot");

        Assert.Collection(
            entries,
            entry => Assert.Equal("Alpha Pilot", entry.CharacterName),
            entry => Assert.Equal("Bravo Pilot", entry.CharacterName),
            entry => Assert.Equal("Charlie Pilot", entry.CharacterName),
            entry => Assert.Equal("Delta Pilot", entry.CharacterName));
    }

    [Fact]
    public void ParseAcceptsStructuredSquadAndRoleRows()
    {
        var entries = RosterPasteParser.Parse(
            "Logi One — Squad 1 — Squad Commander\nScout One | Scouts | Squad Member");

        Assert.Collection(
            entries,
            entry =>
            {
                Assert.Equal("Logi One", entry.CharacterName);
                Assert.Equal("Squad 1", entry.SquadName);
                Assert.Equal("Squad Commander", entry.RoleText);
            },
            entry =>
            {
                Assert.Equal("Scout One", entry.CharacterName);
                Assert.Equal("Scouts", entry.SquadName);
                Assert.Equal("Squad Member", entry.RoleText);
            });
    }
}

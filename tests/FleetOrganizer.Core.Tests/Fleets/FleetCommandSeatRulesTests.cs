using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;

namespace FleetOrganizer.Core.Tests.Fleets;

public sealed class FleetCommandSeatRulesTests
{
    [Theory]
    [InlineData(DesiredFleetRole.WingCommander)]
    [InlineData(DesiredFleetRole.SquadCommander)]
    public void CommanderSeatAcceptsExactlyOnePilot(DesiredFleetRole role)
    {
        Assert.False(FleetCommandSeatRules.AcceptsPilotCount(role, 0));
        Assert.True(FleetCommandSeatRules.AcceptsPilotCount(role, 1));
        Assert.False(FleetCommandSeatRules.AcceptsPilotCount(role, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    public void OrdinarySquadDestinationAcceptsBulkPilotCounts(int pilotCount)
    {
        Assert.True(FleetCommandSeatRules.AcceptsPilotCount(
            DesiredFleetRole.SquadMember,
            pilotCount));
    }
}

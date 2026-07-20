using FleetOrganizer.Core.Domain;

namespace FleetOrganizer.Core.Fleets;

public static class FleetCommandSeatRules
{
    public static bool IsCommandSeat(DesiredFleetRole role) => role is
        DesiredFleetRole.WingCommander or DesiredFleetRole.SquadCommander;

    public static bool AcceptsPilotCount(DesiredFleetRole role, int pilotCount) =>
        pilotCount >= 0 && (!IsCommandSeat(role) || pilotCount == 1);
}

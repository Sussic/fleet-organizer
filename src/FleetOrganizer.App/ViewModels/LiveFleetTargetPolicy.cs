namespace FleetOrganizer.App.ViewModels;

public static class LiveFleetTargetPolicy
{
    public static LiveFleetSquadTargetViewModel[] ForPilotCount(
        IEnumerable<LiveFleetSquadTargetViewModel> targets,
        int pilotCount,
        Func<LiveFleetSquadTargetViewModel, bool>? commandSeatAvailable = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (pilotCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pilotCount));
        }

        return targets.Where(target =>
            !target.IsCommandSeat ||
            (pilotCount == 1 && (commandSeatAvailable?.Invoke(target) ?? true)))
            .ToArray();
    }
}

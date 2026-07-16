namespace FleetOrganizer.Core.Domain;

public sealed record FleetProfile(
    Guid Id,
    string Name,
    IReadOnlyList<ProfileWing> Wings,
    IReadOnlyList<ProfileAssignment> Assignments)
{
    public static FleetProfile Create(string name) =>
        new(Guid.NewGuid(), name, [], []);
}

public sealed record ProfileWing(
    Guid Id,
    string Name,
    int SortOrder,
    IReadOnlyList<ProfileSquad> Squads);

public sealed record ProfileSquad(
    Guid Id,
    string Name,
    int SortOrder);

public sealed record ProfileAssignment(
    long CharacterId,
    string CharacterName,
    Guid TargetSquadId,
    DesiredFleetRole DesiredRole);

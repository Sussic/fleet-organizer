namespace FleetOrganizer.Core.Domain;

public sealed record FleetProfile(
    Guid Id,
    string Name,
    IReadOnlyList<ProfileWing> Wings,
    IReadOnlyList<ProfileAssignment> Assignments)
{
    public IReadOnlyList<ProfileShipRule> ShipRules { get; init; } = [];

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
    DesiredFleetRole DesiredRole)
{
    public string[] Tags { get; init; } = [];
}

public sealed record ProfileShipRule(
    Guid Id,
    string ShipTypeName,
    Guid TargetSquadId,
    int SortOrder)
{
    public string Label { get; init; } = string.Empty;

    public Guid? OverflowSquadId { get; init; }

    public int MaximumPerSquad { get; init; } = 10;

    public bool BalanceAcrossTargets { get; init; }

    public bool IsFallback { get; init; }
}

namespace FleetOrganizer.Core.Planning;

public enum FleetRunMode
{
    FullOrganise,
    InviteMissing,
    PlacePresent,
    FixStructure,
    AssignCommanders,
    ApplyLiveChanges,
}

public enum FleetPlanItemKind
{
    CreateWing,
    RenameWing,
    CreateSquad,
    RenameSquad,
    InviteCharacter,
    MoveCharacter,
    ChangeRole,
    AlreadyCorrect,
    Blocked,
}

public sealed record FleetPlanItem(
    FleetPlanItemKind Kind,
    string Title,
    string Detail,
    long? CharacterId = null,
    Guid? TargetSquadId = null,
    long? LiveWingId = null,
    long? LiveSquadId = null,
    string? TargetWingName = null,
    string? TargetSquadName = null,
    string? PreviousName = null);

public sealed record FleetDryRunPlan(
    Guid ProfileId,
    string ProfileName,
    long FleetId,
    string FleetBossName,
    FleetPlanItem[] Items,
    int IgnoredLiveMembers)
{
    public FleetRunMode Mode { get; init; } = FleetRunMode.FullOrganise;

    public int StructureChanges => Items.Count(item =>
        item.Kind is
            FleetPlanItemKind.CreateWing or
            FleetPlanItemKind.RenameWing or
            FleetPlanItemKind.CreateSquad or
            FleetPlanItemKind.RenameSquad);

    public int StructureCreates => Items.Count(item =>
        item.Kind is FleetPlanItemKind.CreateWing or FleetPlanItemKind.CreateSquad);

    public int StructureRenames => Items.Count(item =>
        item.Kind is FleetPlanItemKind.RenameWing or FleetPlanItemKind.RenameSquad);

    public int CharacterInvites => Items.Count(item =>
        item.Kind == FleetPlanItemKind.InviteCharacter);

    public int CharacterMoves => Items.Count(item =>
        item.Kind == FleetPlanItemKind.MoveCharacter);

    public int RoleChanges => Items.Count(item =>
        item.Kind == FleetPlanItemKind.ChangeRole);

    public int AlreadyCorrect => Items.Count(item =>
        item.Kind == FleetPlanItemKind.AlreadyCorrect);

    public int BlockingIssues => Items.Count(item =>
        item.Kind == FleetPlanItemKind.Blocked);

    public int TotalChanges => StructureChanges + CharacterInvites + CharacterMoves + RoleChanges;

    public bool CanExecute => BlockingIssues == 0;
}

public static class FleetPlanModeFilter
{
    public static FleetDryRunPlan Apply(FleetDryRunPlan plan, FleetRunMode mode)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var keepKinds = mode switch
        {
            FleetRunMode.InviteMissing => new HashSet<FleetPlanItemKind>
            {
                FleetPlanItemKind.InviteCharacter,
                FleetPlanItemKind.Blocked,
            },
            FleetRunMode.PlacePresent => new HashSet<FleetPlanItemKind>
            {
                FleetPlanItemKind.MoveCharacter,
                FleetPlanItemKind.Blocked,
            },
            FleetRunMode.FixStructure => new HashSet<FleetPlanItemKind>
            {
                FleetPlanItemKind.CreateWing,
                FleetPlanItemKind.RenameWing,
                FleetPlanItemKind.CreateSquad,
                FleetPlanItemKind.RenameSquad,
                FleetPlanItemKind.Blocked,
            },
            FleetRunMode.AssignCommanders => new HashSet<FleetPlanItemKind>
            {
                FleetPlanItemKind.ChangeRole,
                FleetPlanItemKind.Blocked,
            },
            FleetRunMode.ApplyLiveChanges => new HashSet<FleetPlanItemKind>
            {
                FleetPlanItemKind.MoveCharacter,
                FleetPlanItemKind.ChangeRole,
                FleetPlanItemKind.Blocked,
            },
            _ => Enum.GetValues<FleetPlanItemKind>().ToHashSet(),
        };
        var items = plan.Items.Where(item => keepKinds.Contains(item.Kind)).ToList();

        if (mode is FleetRunMode.PlacePresent or FleetRunMode.AssignCommanders &&
            plan.StructureChanges > 0)
        {
            items.Add(new FleetPlanItem(
                FleetPlanItemKind.Blocked,
                "Target structure is not ready",
                "Run Fix structure or Full organise first, then refresh this quick operation."));
        }

        if (mode == FleetRunMode.AssignCommanders && plan.CharacterMoves > 0)
        {
            items.Add(new FleetPlanItem(
                FleetPlanItemKind.Blocked,
                "Commander placement is not ready",
                "Run Place joined or Full organise first so commander candidates are in their target squads."));
        }

        if (mode == FleetRunMode.PlacePresent && plan.RoleChanges > 0)
        {
            items.Add(new FleetPlanItem(
                FleetPlanItemKind.Blocked,
                "Commander transitions are excluded",
                "Use Assign commanders or Full organise for commander candidates; Place joined handles ordinary squad members only."));
        }

        return plan with
        {
            Mode = mode,
            Items = items.ToArray(),
        };
    }
}

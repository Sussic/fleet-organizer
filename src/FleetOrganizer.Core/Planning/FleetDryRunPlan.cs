namespace FleetOrganizer.Core.Planning;

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

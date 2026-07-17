using System.Collections.ObjectModel;

namespace FleetOrganizer.App.ViewModels;

public sealed record LiveFleetBoardMemberViewModel(
    long CharacterId,
    string CharacterName,
    string Role,
    string RoleName,
    string ShipTypeName,
    long CurrentWingId,
    long CurrentSquadId,
    string? PendingTarget)
{
    public bool CanStage => string.Equals(Role, "squad_member", StringComparison.Ordinal);

    public string Detail => $"{ShipTypeName} • {RoleName}";

    public bool IsStaged => !string.IsNullOrWhiteSpace(PendingTarget);
}

public sealed record LiveFleetBoardSquadViewModel(
    long WingId,
    long SquadId,
    string WingName,
    string Name,
    ObservableCollection<LiveFleetBoardMemberViewModel> Members)
{
    public string MemberCountText =>
        $"{Members.Count} member{(Members.Count == 1 ? string.Empty : "s")}";
}

public sealed record LiveFleetBoardWingViewModel(
    long WingId,
    string Name,
    ObservableCollection<LiveFleetBoardSquadViewModel> Squads);

public sealed record StagedLiveMoveViewModel(
    long CharacterId,
    string CharacterName,
    long SourceWingId,
    long SourceSquadId,
    long TargetWingId,
    long TargetSquadId,
    string TargetName)
{
    public string Summary => $"{CharacterName} → {TargetName}";
}

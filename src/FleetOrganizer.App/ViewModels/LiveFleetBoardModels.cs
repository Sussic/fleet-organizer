using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FleetOrganizer.App.ViewModels;

public sealed partial class LiveFleetBoardMemberViewModel(
    long characterId,
    string characterName,
    string role,
    string roleName,
    string shipTypeName,
    long currentWingId,
    long currentSquadId,
    string wingName,
    string squadName,
    string? pendingTarget) : ObservableObject
{
    public long CharacterId { get; } = characterId;

    public string CharacterName { get; } = characterName;

    public string Role { get; } = role;

    public string RoleName { get; } = roleName;

    public string ShipTypeName { get; } = shipTypeName;

    public long CurrentWingId { get; } = currentWingId;

    public long CurrentSquadId { get; } = currentSquadId;

    public string WingName { get; } = wingName;

    public string SquadName { get; } = squadName;

    public string? PendingTarget { get; } = pendingTarget;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    public bool CanStage => string.Equals(Role, "squad_member", StringComparison.Ordinal);

    public string Detail => $"{ShipTypeName} • {RoleName}";

    public bool IsStaged => !string.IsNullOrWhiteSpace(PendingTarget);

    public bool Matches(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var value = search.Trim();
        return CharacterName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            ShipTypeName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            RoleName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            WingName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            SquadName.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
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

public sealed record LiveFleetSquadTargetViewModel(
    long WingId,
    long SquadId,
    string DisplayName);

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

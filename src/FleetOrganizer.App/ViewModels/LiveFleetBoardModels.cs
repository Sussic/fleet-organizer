using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FleetOrganizer.Core.Domain;

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
    StagedLiveMoveViewModel? stagedMove) : ObservableObject
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

    public StagedLiveMoveViewModel? StagedMove { get; } = stagedMove;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    public bool CanStage => !string.Equals(Role, "fleet_commander", StringComparison.Ordinal);

    public bool IsCommander => !string.Equals(Role, "squad_member", StringComparison.Ordinal);

    public string Detail => stagedMove is null
        ? $"{ShipTypeName} • {RoleName}"
        : $"{ShipTypeName} • will be {stagedMove.DesiredRoleName}";

    public bool IsStaged => stagedMove is not null;

    public string StagedStatus => stagedMove is null
        ? string.Empty
        : $"MOVED • from {stagedMove.SourceName}";

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

public sealed partial class LiveFleetBoardSquadViewModel(
    long wingId,
    long squadId,
    string wingName,
    string name,
    ObservableCollection<LiveFleetBoardMemberViewModel> members) : ObservableObject
{
    public long WingId { get; } = wingId;

    public long SquadId { get; } = squadId;

    public string WingName { get; } = wingName;

    public string Name { get; } = name;

    public ObservableCollection<LiveFleetBoardMemberViewModel> Members { get; } = members;

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    public string MemberCountText { get; private set; } =
        $"{members.Count} member{(members.Count == 1 ? string.Empty : "s")}";

    public bool IsEmpty => Members.Count == 0;

    public bool CanAcceptDrop => WingId > 0;

    public DesiredFleetRole DropRole => SquadId > 0
        ? DesiredFleetRole.SquadMember
        : DesiredFleetRole.WingCommander;

    public bool IsLiveStructure => WingId > 0 && SquadId > 0;

    public void ApplyFilter(bool isFiltering)
    {
        var visibleCount = Members.Count(member => member.IsVisible);
        IsVisible = !isFiltering || visibleCount > 0;
        MemberCountText = isFiltering
            ? $"{visibleCount} of {Members.Count} shown"
            : $"{Members.Count} member{(Members.Count == 1 ? string.Empty : "s")}";
        OnPropertyChanged(nameof(MemberCountText));
    }
}

public sealed partial class LiveFleetBoardWingViewModel(
    long wingId,
    string name,
    ObservableCollection<LiveFleetBoardSquadViewModel> squads) : ObservableObject
{
    public long WingId { get; } = wingId;

    public string Name { get; } = name;

    public ObservableCollection<LiveFleetBoardSquadViewModel> Squads { get; } = squads;

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    public int MemberCount => Squads.Sum(squad => squad.Members.Count);

    public bool IsEmpty => MemberCount == 0;

    public bool IsLiveStructure => WingId > 0;

    public void ApplyFilter(bool isFiltering)
    {
        foreach (var squad in Squads)
        {
            squad.ApplyFilter(isFiltering);
        }

        IsVisible = !isFiltering || Squads.Any(squad => squad.IsVisible);
    }
}

public sealed record LiveFleetSquadTargetViewModel(
    long WingId,
    long SquadId,
    string DisplayName,
    DesiredFleetRole DesiredRole)
{
    public bool IsCommandSeat => DesiredRole is
        DesiredFleetRole.WingCommander or DesiredFleetRole.SquadCommander;
}

public sealed record StagedLiveMoveViewModel(
    long CharacterId,
    string CharacterName,
    long SourceWingId,
    long SourceSquadId,
    string SourceName,
    long TargetWingId,
    long TargetSquadId,
    string TargetName,
    DesiredFleetRole DesiredRole,
    string PreviousRole)
{
    public bool ChangesRole => !string.Equals(
        PreviousRole,
        ToEsiRole(DesiredRole),
        StringComparison.Ordinal);

    public bool IsHighImpact => ChangesRole &&
        !string.Equals(PreviousRole, "squad_member", StringComparison.Ordinal);

    public string Summary => ChangesRole
        ? $"{CharacterName} → {TargetName} as {HumanizeRole(DesiredRole)}"
        : $"{CharacterName} → {TargetName}";

    public string DesiredRoleName => HumanizeRole(DesiredRole);

    private static string ToEsiRole(DesiredFleetRole role) => role switch
    {
        DesiredFleetRole.SquadCommander => "squad_commander",
        DesiredFleetRole.WingCommander => "wing_commander",
        DesiredFleetRole.FleetCommander => "fleet_commander",
        _ => "squad_member",
    };

    private static string HumanizeRole(DesiredFleetRole role) => role switch
    {
        DesiredFleetRole.SquadCommander => "Squad Commander",
        DesiredFleetRole.WingCommander => "Wing Commander",
        DesiredFleetRole.FleetCommander => "Fleet Commander",
        _ => "Squad Member",
    };
}

public sealed record StagedLiveInviteViewModel(
    long CharacterId,
    string CharacterName,
    long TargetWingId,
    long TargetSquadId,
    string TargetName,
    DesiredFleetRole DesiredRole)
{
    public string Summary => $"Waiting for {CharacterName} → {TargetName}";

    public string RoleName => DesiredRole switch
    {
        DesiredFleetRole.SquadCommander => "Squad Commander",
        DesiredFleetRole.WingCommander => "Wing Commander",
        _ => "Squad Member",
    };
}

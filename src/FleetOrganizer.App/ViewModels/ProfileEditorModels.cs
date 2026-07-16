using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FleetOrganizer.Core.Domain;

namespace FleetOrganizer.App.ViewModels;

public sealed record ProfileListItemViewModel(FleetProfile Profile)
{
    public Guid Id => Profile.Id;

    public string Name => Profile.Name;

    public string Summary =>
        $"{Profile.Assignments.Count} characters • {Profile.Wings.Count} wings";
}

public sealed partial class ProfileWingEditorViewModel(
    Guid id,
    string name) : ObservableObject
{
    public Guid Id { get; } = id;

    [ObservableProperty]
    public partial string Name { get; set; } = name;

    public ObservableCollection<ProfileSquadEditorViewModel> Squads { get; } = [];
}

public sealed partial class ProfileSquadEditorViewModel(
    Guid id,
    string name) : ObservableObject
{
    public Guid Id { get; } = id;

    [ObservableProperty]
    public partial string Name { get; set; } = name;
}

public sealed partial class ProfileAssignmentEditorViewModel(
    long characterId,
    string characterName,
    Guid targetSquadId,
    DesiredFleetRole desiredRole,
    string tagsText) : ObservableObject
{
    public long CharacterId { get; } = characterId;

    public string CharacterName { get; } = characterName;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial Guid TargetSquadId { get; set; } = targetSquadId;

    [ObservableProperty]
    public partial DesiredFleetRole DesiredRole { get; set; } = desiredRole;

    [ObservableProperty]
    public partial string TagsText { get; set; } = tagsText;
}

public sealed record ProfileSquadOptionViewModel(Guid Id, string DisplayName);

public sealed record DesiredRoleOptionViewModel(DesiredFleetRole Value, string DisplayName);

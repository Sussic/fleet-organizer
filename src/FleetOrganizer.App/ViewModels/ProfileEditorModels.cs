using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Operations;
using FleetOrganizer.Core.Planning;

namespace FleetOrganizer.App.ViewModels;

public sealed partial class ProfileListItemViewModel(FleetProfile profile) : ObservableObject
{
    public FleetProfile Profile { get; } = profile;

    public Guid Id => Profile.Id;

    public string Name => Profile.Name;

    public string Summary =>
        $"{Profile.Assignments.Count} characters • {Profile.Wings.Count} wings • " +
        $"{Profile.ShipRules.Count} ship rule{(Profile.ShipRules.Count == 1 ? string.Empty : "s")}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsPinned { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsDefault { get; set; }

    public string StatusText => IsDefault
        ? "DEFAULT"
        : IsPinned
            ? "PINNED"
            : string.Empty;
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

    [ObservableProperty]
    public partial int AssignmentCount { get; set; }

    [ObservableProperty]
    public partial string CharacterPreview { get; set; } = "Drop characters here";

    public string AssignmentCountText =>
        $"{AssignmentCount} character{(AssignmentCount == 1 ? string.Empty : "s")}";

    partial void OnAssignmentCountChanged(int value)
    {
        _ = value;
        OnPropertyChanged(nameof(AssignmentCountText));
    }
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

public sealed partial class ProfileShipRuleEditorViewModel(
    Guid id,
    string shipTypeName,
    Guid targetSquadId) : ObservableObject
{
    public Guid Id { get; } = id;

    [ObservableProperty]
    public partial string ShipTypeName { get; set; } = shipTypeName;

    [ObservableProperty]
    public partial Guid TargetSquadId { get; set; } = targetSquadId;
}

public sealed record DesiredRoleOptionViewModel(DesiredFleetRole Value, string DisplayName);

public sealed record FleetRunModeOptionViewModel(
    FleetRunMode Value,
    string DisplayName,
    string Description);

public sealed record WaitingCharacterViewModel(
    long CharacterId,
    string CharacterName,
    string Target,
    string StateText);

public sealed class FleetAttentionEventArgs(
    string message,
    bool isUrgent) : EventArgs
{
    public string Message { get; } = message;

    public bool IsUrgent { get; } = isUrgent;
}

public sealed record FleetPlanItemViewModel(FleetPlanItem Item)
{
    public string Badge => Item.Kind switch
    {
        FleetPlanItemKind.CreateWing => "CREATE WING",
        FleetPlanItemKind.RenameWing => "RENAME WING",
        FleetPlanItemKind.CreateSquad => "CREATE SQUAD",
        FleetPlanItemKind.RenameSquad => "RENAME SQUAD",
        FleetPlanItemKind.InviteCharacter => "INVITE",
        FleetPlanItemKind.MoveCharacter => "MOVE",
        FleetPlanItemKind.ChangeRole => "ROLE",
        FleetPlanItemKind.AlreadyCorrect => "READY",
        FleetPlanItemKind.Blocked => "BLOCKED",
        _ => "PLAN",
    };

    public string Title => Item.Title;

    public string Detail => Item.Detail;

    public bool IsAlreadyCorrect => Item.Kind == FleetPlanItemKind.AlreadyCorrect;

    public bool IsBlocked => Item.Kind == FleetPlanItemKind.Blocked;
}

public sealed record FleetOperationStepViewModel(FleetOperationStep Step)
{
    public string StepKey => Step.StepKey;

    public string Badge => Step.Type switch
    {
        FleetOperationStepType.CreateWing => "CREATE WING",
        FleetOperationStepType.RenameWing => "RENAME WING",
        FleetOperationStepType.CreateSquad => "CREATE SQUAD",
        FleetOperationStepType.RenameSquad => "RENAME SQUAD",
        FleetOperationStepType.Invite => "INVITE",
        FleetOperationStepType.Place => "PLACE",
        FleetOperationStepType.PromoteCommander => "COMMAND",
        FleetOperationStepType.DeferredCommander => "DEFERRED",
        _ => "STEP",
    };

    public string Title => Step.Type switch
    {
        FleetOperationStepType.CreateWing => $"Create wing for {Step.Target.WingName}",
        FleetOperationStepType.RenameWing => $"Name wing {Step.Target.WingName}",
        FleetOperationStepType.CreateSquad =>
            $"Create squad for {Step.Target.WingName} / {Step.Target.SquadName}",
        FleetOperationStepType.RenameSquad =>
            $"Name squad {Step.Target.WingName} / {Step.Target.SquadName}",
        FleetOperationStepType.Invite => $"Invite {Step.Target.CharacterName}",
        FleetOperationStepType.Place =>
            $"Place {Step.Target.CharacterName} in {Step.Target.WingName} / {Step.Target.SquadName}",
        FleetOperationStepType.DeferredCommander =>
            $"Commander work for {Step.Target.CharacterName}",
        FleetOperationStepType.PromoteCommander =>
            $"Promote {Step.Target.CharacterName} to {GetRoleLabel(Step.Target.DesiredRole)}",
        _ => Step.Target.CharacterName,
    };

    public string StateText => Step.State switch
    {
        FleetOperationStepState.Pending when Step.RetryAfterUtc is DateTimeOffset retryAfter =>
            $"Paused until {retryAfter.ToLocalTime():T}",
        FleetOperationStepState.Pending => "Pending",
        FleetOperationStepState.Running => "Write in progress",
        FleetOperationStepState.Waiting => "Waiting",
        FleetOperationStepState.Succeeded => "Confirmed",
        FleetOperationStepState.Failed => "Needs attention",
        FleetOperationStepState.Skipped => "Skipped",
        _ => Step.State.ToString(),
    };

    public string Detail => Step.Message ?? string.Empty;

    public string AttemptsText => Step.Attempts == 0
        ? string.Empty
        : $" • {Step.Attempts} attempt{(Step.Attempts == 1 ? string.Empty : "s")}";

    public bool IsFailed => Step.State == FleetOperationStepState.Failed;

    public bool CanRetry => IsFailed;

    public bool CanSkip => Step.Type != FleetOperationStepType.DeferredCommander &&
        Step.State is not FleetOperationStepState.Succeeded and not FleetOperationStepState.Skipped;

    private static string GetRoleLabel(DesiredFleetRole role) => role switch
    {
        DesiredFleetRole.SquadCommander => "Squad Commander",
        DesiredFleetRole.WingCommander => "Wing Commander",
        _ => role.ToString(),
    };
}

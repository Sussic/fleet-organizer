using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetOrganizer.App.Services;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Authentication;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Profiles;
using FleetOrganizer.Infrastructure.Authentication;
using FleetOrganizer.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FleetOrganizer.App.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private void ClearPendingLiveChanges()
    {
        pendingLiveChanges.ClearQueued();
        Profiles.HideDryRunCommand.Execute(null);
        LiveCommandStatus = "Queued fleet changes cleared. Sent invitations are still being tracked.";
        LiveApplyFeedback = string.Empty;
        RefreshPendingLiveChangeState();
    }

    [RelayCommand]
    private void QueueLiveWing()
    {
        var name = ValidateLiveStructureName(NewLiveWingName, "wing");
        if (name is null)
        {
            return;
        }

        if (currentSnapshot is null)
        {
            LiveCommandStatus = "Load the live fleet before adding a wing.";
            return;
        }

        if (currentSnapshot.Wings.Any(wing => string.Equals(wing.Name, name, StringComparison.OrdinalIgnoreCase)) ||
            StagedLiveStructureChanges.Any(change =>
                change.Kind == StagedLiveStructureChangeKind.AddWing &&
                string.Equals(change.NewName, name, StringComparison.OrdinalIgnoreCase)))
        {
            LiveCommandStatus = $"A wing named {name} already exists or is queued.";
            return;
        }

        pendingLiveChanges.AddStructureChange(new StagedLiveStructureChangeViewModel(
            Guid.NewGuid(),
            StagedLiveStructureChangeKind.AddWing,
            0,
            0,
            string.Empty,
            string.Empty,
            name));
        NewLiveWingName = string.Empty;
        FinishQueuedStructureChange($"Queued a new wing named {name}.");
    }

    [RelayCommand]
    private void QueueLiveSquad()
    {
        var name = ValidateLiveStructureName(NewLiveSquadName, "squad");
        var targetWing = SelectedStructureWing;
        if (name is null)
        {
            return;
        }

        if (currentSnapshot is null || targetWing is null)
        {
            LiveCommandStatus = "Choose the wing that should receive the new squad.";
            return;
        }

        var liveWing = currentSnapshot.Wings.SingleOrDefault(wing => wing.WingId == targetWing.WingId);
        if (liveWing is null)
        {
            LiveCommandStatus = "That wing is no longer in the live fleet. Refresh and try again.";
            return;
        }

        if (liveWing.Squads.Any(squad => string.Equals(squad.Name, name, StringComparison.OrdinalIgnoreCase)) ||
            StagedLiveStructureChanges.Any(change =>
                change.Kind == StagedLiveStructureChangeKind.AddSquad &&
                change.WingId == targetWing.WingId &&
                string.Equals(change.NewName, name, StringComparison.OrdinalIgnoreCase)))
        {
            LiveCommandStatus = $"{targetWing.Name} already has, or will have, a squad named {name}.";
            return;
        }

        pendingLiveChanges.AddStructureChange(new StagedLiveStructureChangeViewModel(
            Guid.NewGuid(),
            StagedLiveStructureChangeKind.AddSquad,
            targetWing.WingId,
            0,
            targetWing.Name,
            string.Empty,
            name));
        NewLiveSquadName = string.Empty;
        FinishQueuedStructureChange($"Queued {targetWing.Name} / {name}.");
    }

    [RelayCommand]
    private void QueueLiveStructureRename()
    {
        var target = SelectedStructureRenameTarget;
        var name = ValidateLiveStructureName(LiveStructureRenameText, "structure item");
        if (target is null || name is null)
        {
            if (target is null)
            {
                LiveCommandStatus = "Choose the wing or squad to rename.";
            }

            return;
        }

        if (string.Equals(target.Name, name, StringComparison.Ordinal))
        {
            LiveCommandStatus = $"{target.Name} already has that exact name.";
            return;
        }

        var kind = target.Kind == LiveFleetStructureKind.Wing
            ? StagedLiveStructureChangeKind.RenameWing
            : StagedLiveStructureChangeKind.RenameSquad;
        foreach (var previous in StagedLiveStructureChanges
            .Where(change => change.Kind == kind &&
                change.WingId == target.WingId &&
                change.SquadId == target.SquadId)
            .ToArray())
        {
            pendingLiveChanges.RemoveStructureChange(previous);
        }

        pendingLiveChanges.AddStructureChange(new StagedLiveStructureChangeViewModel(
            Guid.NewGuid(),
            kind,
            target.WingId,
            target.SquadId,
            target.WingName,
            target.Name,
            name));
        LiveStructureRenameText = string.Empty;
        FinishQueuedStructureChange($"Queued rename: {target.DisplayName} → {name}.");
    }

    [RelayCommand]
    private void RemoveStagedLiveStructureChange(StagedLiveStructureChangeViewModel? change)
    {
        if (change is null)
        {
            return;
        }

        pendingLiveChanges.RemoveStructureChange(change);
        RefreshPendingLiveChangeState();
        LiveCommandStatus = "Queued structure edit removed.";
    }

    private string? ValidateLiveStructureName(string? value, string kind)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            LiveCommandStatus = $"Enter a {kind} name first.";
            return null;
        }

        if (name.Length > ProfileValidator.MaximumHierarchyNameLength)
        {
            LiveCommandStatus = $"EVE {kind} names can be at most {ProfileValidator.MaximumHierarchyNameLength} characters.";
            return null;
        }

        return name;
    }

    private void FinishQueuedStructureChange(string message)
    {
        SelectedLiveActionTabIndex = 3;
        LiveCommandStatus = message;
        LiveApplyFeedback = "Ready to apply with one confirmation. No ESI write has been sent.";
        RefreshPendingLiveChangeState();
    }

    [RelayCommand]
    private void SelectAllVisibleLiveMembers()
    {
        LiveFleetSelectionModel.SelectAllVisible(GetBoardMembers());
        RefreshLiveSelectionSummary();
    }

    [RelayCommand]
    private void ClearLiveFleetFilter() => LiveFleetSearchText = string.Empty;

    [RelayCommand]
    private void ClearLiveMemberSelection()
    {
        liveSelection.Clear(GetBoardMembers());
        RefreshLiveSelectionSummary();
    }

    [RelayCommand]
    private void StageSelectedLiveMembers()
    {
        if (SelectedBulkLiveTarget is not { } target)
        {
            LiveFleetStatusDetail = "Choose a target squad for the selected live members.";
            return;
        }

        var selected = GetBoardMembers()
            .Where(member => member.IsSelected && member.CanStage)
            .Select(member => member.CharacterId)
            .ToArray();
        if (selected.Length == 0)
        {
            LiveCommandStatus = "Select at least one movable fleet member first.";
            return;
        }

        StageLiveMembers(selected, target.WingId, target.SquadId, target.DesiredRole);
    }

    [RelayCommand]
    private void CancelStagedLiveMember(LiveFleetBoardMemberViewModel? member)
    {
        if (member?.StagedMove is null)
        {
            return;
        }

        pendingLiveChanges.RemoveMove(member.StagedMove);
        LiveCommandStatus = $"Cancelled the staged change for {member.CharacterName}.";
        LiveApplyFeedback = HasPendingLiveChanges
            ? "Queued changes updated. Apply when ready."
            : string.Empty;
        RefreshPendingLiveChangeState();
    }

    [RelayCommand]
    private void RemoveStagedLiveMove(StagedLiveMoveViewModel? move)
    {
        if (move is null)
        {
            return;
        }

        pendingLiveChanges.RemoveMove(move);
        LiveApplyFeedback = HasPendingLiveChanges
            ? "Queued changes updated. Apply when ready."
            : string.Empty;
        RefreshPendingLiveChangeState();
    }

    [RelayCommand]
    private void RemoveStagedLiveInvite(StagedLiveInviteViewModel? invite)
    {
        if (invite is null)
        {
            return;
        }

        pendingLiveChanges.RemoveInvite(invite);
        LiveCommandStatus =
            $"Stopped tracking {invite.CharacterName}. The invitation already sent in EVE cannot be recalled.";
        RefreshPendingLiveChangeState();
    }

    [RelayCommand]
    private async Task InviteNowAsync()
    {
        var snapshot = currentSnapshot;
        if (snapshot is null || SelectedInviteTarget is not { } target)
        {
            LiveCommandStatus = "Load a fleet and choose the invitation target squad first.";
            return;
        }

        var entries = RosterPasteParser.Parse(LiveInviteText);
        if (entries.Length == 0)
        {
            LiveCommandStatus = "Paste at least one exact EVE character name.";
            return;
        }

        IsFleetBusy = true;
        LiveCommandStatus = $"Resolving {entries.Length} exact character name{(entries.Length == 1 ? string.Empty : "s")}…";
        try
        {
            var resolution = await characterNameResolver.ResolveAsync(
                entries.Select(entry => entry.CharacterName).ToArray());
            var liveIds = snapshot.Members.Select(member => member.CharacterId).ToHashSet();
            var trackedIds = StagedLiveInvites.Select(invite => invite.CharacterId).ToHashSet();
            var candidates = resolution.Resolved
                .Where(character =>
                    !liveIds.Contains(character.CharacterId) &&
                    !trackedIds.Contains(character.CharacterId))
                .Select(character => new FleetInvitationCandidate(
                    character.CharacterId,
                    character.CharacterName))
                .ToArray();
            if (candidates.Length == 0)
            {
                LiveInviteText = string.Join(Environment.NewLine, resolution.UnresolvedNames);
                LiveCommandStatus = resolution.UnresolvedNames.Length > 0
                    ? "No invitation was sent. Check the exact character name shown in the box."
                    : "Everyone listed is already in the fleet or already has a tracked invitation.";
                return;
            }

            LiveCommandStatus = $"Sending {candidates.Length} invitation{(candidates.Length == 1 ? string.Empty : "s")}…";
            var result = await fleetInvitationService.InviteAsync(
                snapshot.FleetId,
                target.WingId,
                target.SquadId,
                target.DisplayName,
                target.DesiredRole,
                candidates);
            foreach (var character in result.Sent)
            {
                pendingLiveChanges.AddInvite(new StagedLiveInviteViewModel(
                    character.CharacterId,
                    character.CharacterName,
                    target.WingId,
                    target.SquadId,
                    target.DisplayName,
                    target.DesiredRole));
            }

            LiveInviteText = string.Join(
                Environment.NewLine,
                resolution.UnresolvedNames.Concat(result.Unsent.Select(character => character.CharacterName)));
            var remainingCount = resolution.UnresolvedNames.Length + result.Unsent.Count;
            LiveCommandStatus = remainingCount == 0
                ? result.UserMessage
                : $"{result.UserMessage} {remainingCount} name{(remainingCount == 1 ? " remains" : "s remain")} in the box.";
            RefreshPendingLiveChangeState();
        }
        catch (Exception exception)
        {
            LiveCommandStatus = $"Invitations could not be sent: {exception.Message}";
            await RecordWorkflowFailureAsync("quick-invite", exception);
        }
        finally
        {
            IsFleetBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyPendingLiveChangesAsync()
    {
        var readiness = LiveFleetApplyPolicy.CheckBeforePreparation(
            currentSnapshot is not null,
            HasPendingLiveChanges,
            IsFleetBusy);
        if (readiness == LiveFleetApplyReadiness.NoQueuedChanges)
        {
            LiveCommandStatus = "Drag or select at least one fleet member to queue a change first.";
            LiveApplyFeedback = LiveCommandStatus;
            return;
        }

        if (readiness == LiveFleetApplyReadiness.Busy)
        {
            LiveApplyFeedback = "Fleet Desk is finishing another fleet check. Apply again when the button becomes available.";
            return;
        }

        IsFleetBusy = true;
        LiveApplyFeedback = "Preparing the exact change list and safety checks…";
        LiveCommandStatus = LiveApplyFeedback;
        var snapshot = currentSnapshot!;
        try
        {
            if (!await Profiles.PrepareLiveDeskChangesAsync(
                snapshot,
                StagedLiveMoves.ToArray(),
                [],
                StagedLiveStructureChanges.ToArray()))
            {
                LiveApplyFeedback = Profiles.StatusMessage;
                LiveCommandStatus = LiveApplyFeedback;
                return;
            }

            var blocker = LiveFleetApplyPolicy.GetPreparedRunBlocker(
                Profiles.CanStartReviewedOperation,
                Profiles.HasActiveOperation,
                Profiles.IsBusy,
                Profiles.DryRunBlockingDetails,
                Profiles.StatusMessage);
            if (blocker is not null)
            {
                LiveApplyFeedback = blocker;
                LiveCommandStatus = LiveApplyFeedback;
                return;
            }

            LiveApplyFeedback = "Confirmation opened. Nothing is sent unless you choose Yes.";
            if (await Profiles.StartPreparedOperationAsync())
            {
                pendingLiveChanges.ClearQueued();
                Profiles.HideDryRunCommand.Execute(null);
                await RefreshFleetAsync();
            }

            LiveApplyFeedback = Profiles.StatusMessage;
            LiveCommandStatus = Profiles.StatusMessage;
        }
        catch (Exception exception)
        {
            LiveApplyFeedback = $"Fleet changes could not be prepared: {exception.Message}";
            LiveCommandStatus = LiveApplyFeedback;
            await RecordWorkflowFailureAsync("apply-pending-changes", exception);
        }
        finally
        {
            IsFleetBusy = false;
            RefreshPendingLiveChangeState();
        }
    }

    [RelayCommand]
    private async Task PreviewSelectedTemplateAsync()
    {
        await Profiles.PrepareFleetCommand.ExecuteAsync(null);
        LiveCommandStatus = Profiles.StatusMessage;
    }

    public void StageLiveMemberMove(
        long characterId,
        long targetWingId,
        long targetSquadId,
        DesiredFleetRole desiredRole)
    {
        var snapshot = currentSnapshot;
        if (snapshot is null)
        {
            return;
        }

        var member = snapshot.Members.FirstOrDefault(candidate => candidate.CharacterId == characterId);
        var wing = snapshot.Wings.FirstOrDefault(candidate => candidate.WingId == targetWingId);
        var squad = targetSquadId > 0
            ? wing?.Squads.FirstOrDefault(candidate => candidate.SquadId == targetSquadId)
            : null;
        var targetIsValid = desiredRole == DesiredFleetRole.WingCommander
            ? wing is not null
            : wing is not null && squad is not null;
        if (member is null || !targetIsValid)
        {
            LiveFleetStatusDetail = "That character or target position is no longer in the current fleet. Refresh and try again.";
            return;
        }

        if (desiredRole == DesiredFleetRole.FleetCommander)
        {
            LiveCommandStatus = "Fleet-boss transfer is a separately locked high-impact action.";
            return;
        }

        if (desiredRole is DesiredFleetRole.WingCommander or DesiredFleetRole.SquadCommander &&
            IsCommandSeatReservedForAnother(
                characterId,
                targetWingId,
                targetSquadId,
                desiredRole))
        {
            LiveCommandStatus = desiredRole == DesiredFleetRole.WingCommander
                ? $"{wing!.Name} already has a wing commander. Stage that commander out first."
                : $"{wing!.Name} / {squad!.Name} already has a squad commander. Stage that commander out first.";
            LiveApplyFeedback = LiveCommandStatus;
            return;
        }

        var existingMove = StagedLiveMoves.FirstOrDefault(move => move.CharacterId == characterId);
        if (existingMove is not null)
        {
            pendingLiveChanges.RemoveMove(existingMove);
        }
        if (member.WingId == targetWingId &&
            (desiredRole == DesiredFleetRole.WingCommander || member.SquadId == targetSquadId) &&
            DesiredRoleForEsiRole(member.Role) == desiredRole)
        {
            RefreshPendingLiveChangeState();
            LiveCommandStatus = $"{member.CharacterName} is already correct.";
            return;
        }

        var targetCount = snapshot.Members.Count(candidate =>
        {
            var staged = StagedLiveMoves.FirstOrDefault(move => move.CharacterId == candidate.CharacterId);
            return staged is not null
                ? staged.TargetWingId == targetWingId && staged.TargetSquadId == targetSquadId
                : candidate.WingId == targetWingId && candidate.SquadId == targetSquadId;
        });
        var isAlreadyInTarget = member.WingId == targetWingId && member.SquadId == targetSquadId;
        if (desiredRole != DesiredFleetRole.WingCommander &&
            !isAlreadyInTarget &&
            targetCount >= FleetOrganizer.Core.Profiles.ProfileValidator.MaximumCharactersPerSquad)
        {
            RefreshPendingLiveChangeState();
            LiveCommandStatus =
                $"{wing!.Name} / {squad!.Name} is already at EVE's {FleetOrganizer.Core.Profiles.ProfileValidator.MaximumCharactersPerSquad}-pilot squad limit.";
            return;
        }

        var sourceName = GetLivePlacementName(snapshot, member);
        var targetName = desiredRole == DesiredFleetRole.WingCommander
            ? $"{wing!.Name} / Wing Command"
            : desiredRole == DesiredFleetRole.SquadCommander
                ? $"{wing!.Name} / {squad!.Name} / Squad Commander"
                : $"{wing!.Name} / {squad!.Name}";

        pendingLiveChanges.AddMove(new StagedLiveMoveViewModel(
            characterId,
            member.CharacterName,
            member.WingId,
            member.SquadId,
            sourceName,
            targetWingId,
            targetSquadId,
            targetName,
            desiredRole,
            member.Role));
        RefreshPendingLiveChangeState();
        SelectedLiveActionTabIndex = 2;
        LiveCommandStatus = $"Staged {member.CharacterName} → {targetName}.";
        LiveApplyFeedback = "Ready to apply. The next click prepares one confirmation; no ESI write happens before Yes.";
    }

    private bool IsCommandSeatReservedForAnother(
        long characterId,
        long targetWingId,
        long targetSquadId,
        DesiredFleetRole desiredRole)
    {
        if (currentSnapshot is null)
        {
            return true;
        }

        var occupiedByMember = currentSnapshot.Members
            .Where(member => member.CharacterId != characterId)
            .Any(member =>
            {
                var staged = StagedLiveMoves.FirstOrDefault(move =>
                    move.CharacterId == member.CharacterId);
                var role = staged?.DesiredRole ?? DesiredRoleForEsiRole(member.Role);
                var wingId = staged?.TargetWingId ?? member.WingId;
                var squadId = staged?.TargetSquadId ?? member.SquadId;
                return role == desiredRole &&
                    wingId == targetWingId &&
                    (desiredRole == DesiredFleetRole.WingCommander ||
                        squadId == targetSquadId);
            });
        var reservedByInvite = StagedLiveInvites.Any(invite =>
            invite.CharacterId != characterId &&
            invite.DesiredRole == desiredRole &&
            invite.TargetWingId == targetWingId &&
            (desiredRole == DesiredFleetRole.WingCommander ||
                invite.TargetSquadId == targetSquadId));
        return occupiedByMember || reservedByInvite;
    }

    public void StageDraggedLiveMembers(
        long draggedCharacterId,
        long targetWingId,
        long targetSquadId,
        DesiredFleetRole desiredRole)
    {
        var dragged = GetBoardMembers().FirstOrDefault(member => member.CharacterId == draggedCharacterId);
        var characterIds = dragged is { IsSelected: true }
            ? GetBoardMembers()
                .Where(member => member.IsSelected && member.CanStage)
                .Select(member => member.CharacterId)
                .ToArray()
            : [draggedCharacterId];
        StageLiveMembers(characterIds, targetWingId, targetSquadId, desiredRole);
    }

    public void SelectLiveMember(long characterId, bool extendRange, bool toggle)
    {
        liveSelection.Select(
            GetBoardMembers().ToArray(),
            characterId,
            extendRange,
            toggle);
        RefreshLiveSelectionSummary();
    }

    private void StageLiveMembers(
        long[] characterIds,
        long targetWingId,
        long targetSquadId,
        DesiredFleetRole? desiredRole)
    {
        if (desiredRole is DesiredFleetRole role &&
            !FleetCommandSeatRules.AcceptsPilotCount(role, characterIds.Length))
        {
            LiveCommandStatus = "A commander seat accepts exactly one pilot. Select one pilot, or choose a squad-member destination for the group.";
            LiveApplyFeedback = LiveCommandStatus;
            return;
        }

        foreach (var characterId in characterIds)
        {
            var member = currentSnapshot?.Members.FirstOrDefault(candidate => candidate.CharacterId == characterId);
            StageLiveMemberMove(
                characterId,
                targetWingId,
                targetSquadId,
                desiredRole ?? DesiredRoleForEsiRole(member?.Role));
        }

        ClearLiveMemberSelection();
    }

    private static DesiredFleetRole DesiredRoleForEsiRole(string? role) => role switch
    {
        "squad_commander" => DesiredFleetRole.SquadCommander,
        "wing_commander" => DesiredFleetRole.WingCommander,
        "fleet_commander" => DesiredFleetRole.FleetCommander,
        _ => DesiredFleetRole.SquadMember,
    };

    [RelayCommand]
    private async Task ApplyFleetSettingsAsync()
    {
        if (DetectedFleetId is not long fleetId || !HasFleetSettingsChanges)
        {
            LiveCommandStatus = "Change free move or the fleet MOTD first.";
            return;
        }

        if (!userInteraction.Confirm(
            "Apply fleet settings",
            $"Apply these fleet settings?\n\nFree move: {(FleetSettingsFreeMove ? "On" : "Off")}\nMOTD: {FleetSettingsMotd}",
            UserConfirmationKind.Question))
        {
            return;
        }

        if (await RunAdministrativeActionAsync(() =>
            fleetAdministrationService.UpdateFleetSettingsAsync(
                fleetId,
                FleetSettingsFreeMove,
                FleetSettingsMotd)))
        {
            HasFleetSettingsChanges = false;
        }
    }

    [RelayCommand]
    private async Task KickSelectedLiveMembersAsync()
    {
        var selected = GetBoardMembers()
            .Where(member => member.IsSelected)
            .ToArray();
        if (!CanStartHighImpactAction(selected.Length > 0))
        {
            LiveCommandStatus = selected.Length == 0
                ? "Select the fleet members to kick first."
                : "Tick ‘Unlock high-impact actions’ first.";
            return;
        }

        var names = string.Join(", ", selected.Take(8).Select(member => member.CharacterName));
        if (selected.Length > 8)
        {
            names += $" and {selected.Length - 8} more";
        }

        if (!userInteraction.Confirm(
            "Confirm fleet kicks",
            $"Kick {selected.Length} selected fleet member{(selected.Length == 1 ? string.Empty : "s")} immediately?\n\n{names}\n\nThis is not a staged move: accepted kicks take effect immediately and are not automatically undone.",
            UserConfirmationKind.Warning))
        {
            return;
        }

        await RunAdministrativeActionAsync(() => fleetAdministrationService.KickMembersAsync(
            DetectedFleetId!.Value,
            selected.Select(member => member.CharacterId).ToArray()));
    }

    [RelayCommand]
    private async Task TransferFleetBossToSelectedAsync()
    {
        var selected = GetBoardMembers().Where(member => member.IsSelected).ToArray();
        if (!CanStartHighImpactAction(selected.Length == 1))
        {
            LiveCommandStatus = selected.Length != 1
                ? "Select exactly one character to receive fleet boss."
                : "Tick ‘Unlock high-impact actions’ first.";
            return;
        }

        var target = selected[0];
        if (!userInteraction.Confirm(
            "Confirm fleet-boss transfer",
            $"Transfer fleet boss to {target.CharacterName}?\n\nThe signed-in character will immediately lose write access. Any other pending Fleet Desk work should be completed first.",
            UserConfirmationKind.Warning))
        {
            return;
        }

        await RunAdministrativeActionAsync(() => fleetAdministrationService.TransferFleetBossAsync(
            DetectedFleetId!.Value,
            target.CharacterId));
    }

    [RelayCommand]
    private async Task DeleteLiveSquadAsync(LiveFleetBoardSquadViewModel? squad)
    {
        if (squad is null || !squad.IsLiveStructure || !CanStartHighImpactAction(squad.IsLiveEmpty))
        {
            LiveCommandStatus = squad is { IsLiveEmpty: false }
                ? "That squad is not empty. Move or kick its members first."
                : "Unlock high-impact actions before deleting an empty squad.";
            return;
        }

        if (!userInteraction.Confirm(
            "Confirm squad deletion",
            $"Delete the empty squad {squad.WingName} / {squad.Name}?\n\nThis removes the live EVE hierarchy item immediately.",
            UserConfirmationKind.Warning))
        {
            return;
        }

        await RunAdministrativeActionAsync(() => fleetAdministrationService.DeleteSquadAsync(
            DetectedFleetId!.Value,
            squad.SquadId));
    }

    [RelayCommand]
    private async Task DeleteLiveWingAsync(LiveFleetBoardWingViewModel? wing)
    {
        if (wing is null || !wing.IsLiveStructure || !CanStartHighImpactAction(wing.IsLiveEmpty))
        {
            LiveCommandStatus = wing is { IsLiveEmpty: false }
                ? "That wing is not empty. Move or kick its members first."
                : "Unlock high-impact actions before deleting an empty wing.";
            return;
        }

        if (!userInteraction.Confirm(
            "Confirm wing deletion",
            $"Delete the empty wing {wing.Name}?\n\nThis removes the wing and its empty squads from the live EVE fleet immediately.",
            UserConfirmationKind.Warning))
        {
            return;
        }

        await RunAdministrativeActionAsync(() => fleetAdministrationService.DeleteWingAsync(
            DetectedFleetId!.Value,
            wing.WingId));
    }

    [RelayCommand]
    private async Task CleanRebuildFleetAsync()
    {
        var snapshot = currentSnapshot;
        var profile = Profiles.GetSelectedProfileSnapshot();
        if (snapshot is null || DetectedFleetId is null)
        {
            LiveCommandStatus = "Load the live fleet before starting a clean rebuild.";
            return;
        }

        if (profile is null)
        {
            LiveCommandStatus = "Choose a saved setup before starting a clean rebuild.";
            return;
        }

        if (Profiles.HasUnsavedChanges)
        {
            LiveCommandStatus = "Save the setup first so the exact rebuild plan has a durable local record.";
            return;
        }

        if (HasPendingLiveChanges)
        {
            LiveCommandStatus = "Apply or cancel the currently staged moves before starting a clean rebuild.";
            return;
        }

        if (!CanStartHighImpactAction(targetIsValid: true))
        {
            LiveCommandStatus = "Unlock high-impact actions before starting a clean rebuild.";
            return;
        }

        var effectiveProfile = ShipRuleResolver.Resolve(profile, snapshot).EffectiveProfile;
        var knownIds = effectiveProfile.Assignments.Select(assignment => assignment.CharacterId).ToHashSet();
        var unknownCount = snapshot.Members.Count(member => !knownIds.Contains(member.CharacterId));
        var invitationCount = effectiveProfile.Assignments.Count(assignment =>
            snapshot.Members.All(member => member.CharacterId != assignment.CharacterId));
        var squadCount = profile.Wings.Sum(wing => wing.Squads.Count);
        if (!userInteraction.Confirm(
            "Confirm clean fleet rebuild",
            $"Clean-rebuild fleet {snapshot.FleetId} from '{profile.Name}'?\n\n" +
            $"1. Move and temporarily demote {snapshot.Members.Length} live pilots into Unknown.\n" +
            "2. Delete the now-empty old wings and squads.\n" +
            $"3. Create {profile.Wings.Count} wings and {squadCount} squads.\n" +
            $"4. Place known and ship-rule pilots, restore commanders, and send {invitationCount} invitation{(invitationCount == 1 ? string.Empty : "s")}.\n" +
            $"5. Leave {unknownCount} unmatched pilot{(unknownCount == 1 ? string.Empty : "s")} safely in Unknown.\n\n" +
            "This is deliberately destructive and may take a while. If ESI stops a write, run Clean rebuild again after refreshing; Fleet Desk will reuse Unknown and continue from the live state.",
            UserConfirmationKind.Warning))
        {
            return;
        }

        IsFleetBusy = true;
        UnlockHighImpactActions = false;
        LiveCommandStatus = "Starting guarded clean rebuild…";
        try
        {
            var progress = new Progress<FleetRebuildProgress>(update =>
            {
                LiveCommandStatus = $"Clean rebuild • {update.CompletedWrites} writes • {update.Message}";
            });
            var result = await fleetRebuildService.RebuildAsync(
                snapshot.FleetId,
                profile,
                progress);
            LiveCommandStatus = result.UserMessage;
        }
        catch (Exception exception)
        {
            LiveCommandStatus = $"Clean rebuild stopped: {exception.Message}";
            await RecordWorkflowFailureAsync("clean-rebuild", exception);
        }
        finally
        {
            IsFleetBusy = false;
            await RefreshFleetAsync();
        }
    }

    private bool CanStartHighImpactAction(bool targetIsValid) =>
        targetIsValid && UnlockHighImpactActions && DetectedFleetId.HasValue && !IsFleetBusy;

    private async Task<bool> RunAdministrativeActionAsync(
        Func<Task<FleetAdministrationResult>> action)
    {
        IsFleetBusy = true;
        LiveCommandStatus = "Re-checking fleet identity and fleet-boss authority…";
        try
        {
            var result = await action();
            LiveCommandStatus = result.UserMessage;
            UnlockHighImpactActions = false;
            await RefreshFleetAsync();
            return result.IsSuccess;
        }
        catch (Exception exception)
        {
            LiveCommandStatus = $"Fleet action failed: {exception.Message}";
            await RecordWorkflowFailureAsync("fleet-administration", exception);
            return false;
        }
        finally
        {
            IsFleetBusy = false;
        }
    }

    private async Task RecordWorkflowFailureAsync(string action, Exception exception)
    {
        try
        {
            await workflowDiagnosticLog.WriteFailureAsync(action, DetectedFleetId, exception);
        }
        catch (Exception loggingException) when (
            loggingException is IOException or UnauthorizedAccessException)
        {
            // Diagnostics must never hide or replace the workflow error already shown to the FC.
        }
    }

    partial void OnLiveFleetSearchTextChanged(string value)
    {
        foreach (var member in GetBoardMembers())
        {
            member.IsVisible = member.Matches(value);
        }

        ApplyLiveFleetFilter();
        RefreshLiveSelectionSummary();
    }

    partial void OnLiveInviteTextChanged(string value)
    {
        _ = value;
        RefreshLiveInviteTargets();
    }

    partial void OnFleetSettingsFreeMoveChanged(bool value)
    {
        _ = value;
        RefreshFleetSettingsChangeFlag();
    }

    partial void OnFleetSettingsMotdChanged(string value)
    {
        _ = value;
        RefreshFleetSettingsChangeFlag();
    }

    private void RefreshFleetSettingsChangeFlag()
    {
        if (isApplyingFleetSettings || currentSnapshot is null)
        {
            return;
        }

        HasFleetSettingsChanges = FleetSettingsFreeMove != currentSnapshot.IsFreeMove ||
            !string.Equals(FleetSettingsMotd, currentSnapshot.Motd, StringComparison.Ordinal);
    }

    private void ApplySnapshotFleetSettings(LiveFleetSnapshot snapshot)
    {
        if (HasFleetSettingsChanges)
        {
            return;
        }

        isApplyingFleetSettings = true;
        FleetSettingsFreeMove = snapshot.IsFreeMove;
        FleetSettingsMotd = snapshot.Motd;
        HasFleetSettingsChanges = false;
        isApplyingFleetSettings = false;
    }

}

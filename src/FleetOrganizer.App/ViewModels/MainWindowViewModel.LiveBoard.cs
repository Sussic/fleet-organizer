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
    private void ApplyLiveFleetResult(LiveFleetLoadResult result)
    {
        if (result.Status != LiveFleetLoadStatus.Failed || currentSnapshot is null)
        {
            FleetHierarchy.Clear();
        }
        IsLiveFleetReady = result.Status == LiveFleetLoadStatus.Ready;

        var snapshot = result.Snapshot;
        if (snapshot is null)
        {
            if (result.Status == LiveFleetLoadStatus.Failed && currentSnapshot is not null)
            {
                LiveFleetFreshness =
                    $"Last confirmed {currentSnapshot.ConfirmedAtUtc.ToLocalTime():t} • automatic retry pending";
            }
            else
            {
                currentSnapshot = null;
                pendingLiveChanges.Reset();
                liveSelection.ResetAnchor();
                ClearFleetBoard();
                LiveFleetSquadTargets.Clear();
                LiveBulkMoveTargets.Clear();
                LiveInviteTargets.Clear();
                DetectedFleetId = null;
                LiveFleetBoss = "Not detected";
                LiveFleetSummary = "No fleet data loaded";
                LiveFleetFreshness = result.CheckedAtUtc is DateTimeOffset checkedAtUtc
                    ? $"Checked {checkedAtUtc.ToLocalTime():t} • automatic every {Profiles.FleetPollingSeconds} seconds while open"
                    : "Not refreshed yet";
                RefreshPendingLiveChangeState();
            }
        }
        else
        {
            if (currentSnapshot?.FleetId != snapshot.FleetId)
            {
                pendingLiveChanges.Reset();
                liveSelection.ResetAnchor();
                HasFleetSettingsChanges = false;
            }

            currentSnapshot = snapshot;
            ApplySnapshotFleetSettings(snapshot);
            var reconciliation = LiveFleetReconciliation.Apply(pendingLiveChanges, snapshot);
            if (reconciliation.AcceptedInvites > 0)
            {
                LiveCommandStatus =
                    $"{reconciliation.AcceptedInvites} invited pilot{(reconciliation.AcceptedInvites == 1 ? " has" : "s have")} joined the fleet.";
            }
            DetectedFleetId = snapshot.FleetId;
            LiveFleetBoss = $"{snapshot.FleetBossName} ({snapshot.FleetBossId})";
            LiveFleetFreshness =
                $"Confirmed {snapshot.ConfirmedAtUtc.ToLocalTime():t} • automatic every {Profiles.FleetPollingSeconds} seconds while open";
            LiveFleetSummary = result.Status == LiveFleetLoadStatus.Ready
                ? $"{snapshot.Members.Length} members • free move {(snapshot.IsFreeMove ? "on" : "off")} • advert {(snapshot.IsRegistered ? "active" : "off")} • voice {(snapshot.IsVoiceEnabled ? "on" : "off")}"
                : "Fleet detected • detailed hierarchy requires the signed-in character to be fleet boss";
        }

        LiveFleetStatusTitle = result.Status switch
        {
            LiveFleetLoadStatus.Ready => $"Fleet {snapshot!.FleetId} is live",
            LiveFleetLoadStatus.SignedOut => "Sign in to read a fleet",
            LiveFleetLoadStatus.NotInFleet => "No current fleet detected",
            LiveFleetLoadStatus.NotFleetBoss => "Fleet detected — fleet boss access required",
            _ => "Live fleet could not be refreshed",
        };
        LiveFleetStatusDetail = result.RetryAfter is null
            ? result.UserMessage
            : $"{result.UserMessage} Automatic refresh will resume when ESI allows it.";

        if (snapshot is not null && result.Status == LiveFleetLoadStatus.Ready)
        {
            foreach (var node in BuildFleetTree(snapshot))
            {
                FleetHierarchy.Add(node);
            }

            BuildFleetBoard(snapshot);
            RefreshPendingLiveChangeState();
        }
    }

    private void ClearLiveFleet()
    {
        FleetHierarchy.Clear();
        ClearFleetBoard();
        LiveFleetSquadTargets.Clear();
        LiveBulkMoveTargets.Clear();
        LiveInviteTargets.Clear();
        LiveStructureWings.Clear();
        LiveStructureTargets.Clear();
        pendingLiveChanges.Reset();
        liveSelection.ResetAnchor();
        currentSnapshot = null;
        DetectedFleetId = null;
        IsLiveFleetReady = false;
        LiveFleetBoss = "Not detected";
        LiveFleetSummary = "No fleet data loaded";
        LiveFleetFreshness = "Not refreshed yet";
        LiveFleetStatusTitle = "Sign in to read a fleet";
        LiveFleetStatusDetail = "Authorize the character that will be fleet boss.";
        RefreshPendingLiveChangeState();
    }

    private void BuildFleetBoard(LiveFleetSnapshot snapshot)
    {
        var selectedIds = GetBoardMembers()
            .Where(member => member.IsSelected)
            .Select(member => member.CharacterId)
            .ToHashSet();
        ClearFleetBoard();
        LiveFleetSquadTargets.Clear();
        LiveStructureWings.Clear();
        LiveStructureTargets.Clear();

        var fleetCommandMembers = CreateBoardMembers(
            snapshot.Members.Where(member =>
                GetEffectivePosition(member).WingId < 0),
            "Fleet Command",
            "Fleet Command",
            selectedIds);
        if (fleetCommandMembers.Count > 0)
        {
            FleetBoardWings.Add(new LiveFleetBoardWingViewModel(
                -1,
                "Fleet Command",
                isLiveEmpty: false,
                [new LiveFleetBoardSquadViewModel(
                    -1,
                    -1,
                    "Fleet Command",
                    "Command",
                    isLiveEmpty: false,
                    fleetCommandMembers)]));
        }

        foreach (var wing in snapshot.Wings)
        {
            LiveStructureWings.Add(new LiveFleetWingTargetViewModel(wing.WingId, wing.Name));
            LiveStructureTargets.Add(new LiveFleetStructureTargetViewModel(
                LiveFleetStructureKind.Wing,
                wing.WingId,
                0,
                wing.Name,
                wing.Name));
            var squads = new ObservableCollection<LiveFleetBoardSquadViewModel>();
            var wingCommandMembers = CreateBoardMembers(
                snapshot.Members.Where(member =>
                {
                    var position = GetEffectivePosition(member);
                    return position.WingId == wing.WingId && position.SquadId < 0;
                }),
                wing.Name,
                "Wing Command",
                selectedIds);
            squads.Add(new LiveFleetBoardSquadViewModel(
                wing.WingId,
                -1,
                wing.Name,
                "Wing Command",
                isLiveEmpty: false,
                wingCommandMembers));
            LiveFleetSquadTargets.Add(new LiveFleetSquadTargetViewModel(
                wing.WingId,
                -1,
                $"{wing.Name} — Wing Commander",
                DesiredFleetRole.WingCommander));

            foreach (var squad in wing.Squads)
            {
                LiveStructureTargets.Add(new LiveFleetStructureTargetViewModel(
                    LiveFleetStructureKind.Squad,
                    wing.WingId,
                    squad.SquadId,
                    wing.Name,
                    squad.Name));
                var members = CreateBoardMembers(
                    snapshot.Members.Where(member =>
                    {
                        var position = GetEffectivePosition(member);
                        return position.WingId == wing.WingId &&
                            position.SquadId == squad.SquadId;
                    }),
                    wing.Name,
                    squad.Name,
                    selectedIds);
                squads.Add(new LiveFleetBoardSquadViewModel(
                    wing.WingId,
                    squad.SquadId,
                    wing.Name,
                    squad.Name,
                    isLiveEmpty: LiveFleetOccupancyPolicy.IsSquadActuallyEmpty(snapshot, squad.SquadId),
                    members));
                LiveFleetSquadTargets.Add(new LiveFleetSquadTargetViewModel(
                    wing.WingId,
                    squad.SquadId,
                    $"{wing.Name} / {squad.Name} — Squad Commander",
                    DesiredFleetRole.SquadCommander));
                LiveFleetSquadTargets.Add(new LiveFleetSquadTargetViewModel(
                    wing.WingId,
                    squad.SquadId,
                    $"{wing.Name} / {squad.Name} — Squad members",
                    DesiredFleetRole.SquadMember));
            }

            FleetBoardWings.Add(new LiveFleetBoardWingViewModel(
                wing.WingId,
                wing.Name,
                isLiveEmpty: LiveFleetOccupancyPolicy.IsWingActuallyEmpty(snapshot, wing.WingId),
                squads));
        }

        RefreshLiveBulkMoveTargets();
        RefreshLiveInviteTargets();
        SelectedStructureWing = LiveStructureWings.FirstOrDefault(candidate =>
            candidate.WingId == SelectedStructureWing?.WingId) ?? LiveStructureWings.FirstOrDefault();
        SelectedStructureRenameTarget = LiveStructureTargets.FirstOrDefault(candidate =>
            candidate.Kind == SelectedStructureRenameTarget?.Kind &&
            candidate.WingId == SelectedStructureRenameTarget?.WingId &&
            candidate.SquadId == SelectedStructureRenameTarget?.SquadId) ?? LiveStructureTargets.FirstOrDefault();
        ApplyLiveFleetFilter();
        RefreshLiveSelectionSummary();
    }

    private void RefreshLiveInviteTargets()
    {
        var selected = SelectedInviteTarget;
        var nameCount = RosterPasteParser.Parse(LiveInviteText).Length;
        LiveInviteTargets.Clear();

        foreach (var target in LiveFleetTargetPolicy.ForPilotCount(
            LiveFleetSquadTargets,
            nameCount,
            IsLiveCommandSeatAvailable))
        {
            LiveInviteTargets.Add(target);
        }

        SelectedInviteTarget = LiveInviteTargets.FirstOrDefault(target =>
            selected is not null &&
            target.WingId == selected.WingId &&
            target.SquadId == selected.SquadId &&
            target.DesiredRole == selected.DesiredRole) ?? LiveInviteTargets
                .FirstOrDefault(target => target.DesiredRole == DesiredFleetRole.SquadMember);
        OnPropertyChanged(nameof(LiveInviteTargetHint));
    }

    private void RefreshLiveBulkMoveTargets()
    {
        var selected = SelectedBulkLiveTarget;
        var selectedCount = GetBoardMembers().Count(member => member.IsSelected && member.CanStage);
        LiveBulkMoveTargets.Clear();
        foreach (var target in LiveFleetTargetPolicy.ForPilotCount(
            LiveFleetSquadTargets,
            selectedCount))
        {
            LiveBulkMoveTargets.Add(target);
        }

        SelectedBulkLiveTarget = LiveBulkMoveTargets.FirstOrDefault(target =>
            selected is not null &&
            target.WingId == selected.WingId &&
            target.SquadId == selected.SquadId &&
            target.DesiredRole == selected.DesiredRole) ?? LiveBulkMoveTargets
                .FirstOrDefault(target => target.DesiredRole == DesiredFleetRole.SquadMember);
    }

    private bool IsLiveCommandSeatAvailable(LiveFleetSquadTargetViewModel target)
    {
        if (!target.IsCommandSeat || currentSnapshot is null)
        {
            return false;
        }

        var isOccupiedInEve = currentSnapshot.Members.Any(member =>
            DesiredRoleForEsiRole(member.Role) == target.DesiredRole &&
            member.WingId == target.WingId &&
            (target.DesiredRole == DesiredFleetRole.WingCommander ||
                member.SquadId == target.SquadId));
        var isReservedByMove = StagedLiveMoves.Any(move =>
            move.DesiredRole == target.DesiredRole &&
            move.TargetWingId == target.WingId &&
            (target.DesiredRole == DesiredFleetRole.WingCommander ||
                move.TargetSquadId == target.SquadId));
        var isReservedByInvite = StagedLiveInvites.Any(invite =>
            invite.DesiredRole == target.DesiredRole &&
            invite.TargetWingId == target.WingId &&
            (target.DesiredRole == DesiredFleetRole.WingCommander ||
                invite.TargetSquadId == target.SquadId));
        return !isOccupiedInEve && !isReservedByMove && !isReservedByInvite;
    }

    private void ApplyLiveFleetFilter()
    {
        var isFiltering = !string.IsNullOrWhiteSpace(LiveFleetSearchText);
        foreach (var wing in FleetBoardWings)
        {
            wing.ApplyFilter(isFiltering);
        }

        RebuildFleetBoardRows();
    }

    private ObservableCollection<LiveFleetBoardMemberViewModel> CreateBoardMembers(
        IEnumerable<LiveFleetMember> source,
        string wingName,
        string squadName,
        HashSet<long> selectedIds)
    {
        var members = new ObservableCollection<LiveFleetBoardMemberViewModel>(
            SortMembers(source.ToArray()).Select(member =>
            {
                var staged = StagedLiveMoves.FirstOrDefault(move =>
                    move.CharacterId == member.CharacterId);
                return new LiveFleetBoardMemberViewModel(
                    member.CharacterId,
                    member.CharacterName,
                    member.Role,
                    member.RoleName,
                    member.ShipTypeName,
                    member.WingId,
                    member.SquadId,
                    wingName,
                    squadName,
                    staged);
            }));
        foreach (var member in members)
        {
            member.IsSelected = selectedIds.Contains(member.CharacterId);
            member.IsVisible = member.Matches(LiveFleetSearchText);
            member.PropertyChanged += OnLiveFleetMemberPropertyChanged;
        }

        return members;
    }

    private (long WingId, long SquadId) GetEffectivePosition(LiveFleetMember member)
    {
        var staged = StagedLiveMoves.FirstOrDefault(move => move.CharacterId == member.CharacterId);
        if (staged is null)
        {
            return (member.WingId, member.SquadId);
        }

        return staged.DesiredRole == DesiredFleetRole.WingCommander
            ? (staged.TargetWingId, -1)
            : (staged.TargetWingId, staged.TargetSquadId);
    }

    private static string GetLivePlacementName(
        LiveFleetSnapshot snapshot,
        LiveFleetMember member)
    {
        if (member.WingId < 0)
        {
            return "Fleet Command";
        }

        var wing = snapshot.Wings.FirstOrDefault(candidate => candidate.WingId == member.WingId);
        if (wing is null)
        {
            return "Unknown position";
        }

        if (member.SquadId < 0)
        {
            return $"{wing.Name} / Wing Command";
        }

        var squad = wing.Squads.FirstOrDefault(candidate => candidate.SquadId == member.SquadId);
        return squad is null
            ? $"{wing.Name} / Unknown squad"
            : $"{wing.Name} / {squad.Name}";
    }

    private IEnumerable<LiveFleetBoardMemberViewModel> GetBoardMembers() =>
        FleetBoardWings.SelectMany(wing => wing.Squads).SelectMany(squad => squad.Members);

    private void ClearFleetBoard()
    {
        foreach (var member in GetBoardMembers())
        {
            member.PropertyChanged -= OnLiveFleetMemberPropertyChanged;
        }

        FleetBoardWings.Clear();
        FleetBoardRows.Clear();
    }

    private void RebuildFleetBoardRows()
    {
        FleetBoardRows.Clear();
        foreach (var row in LiveFleetBoardProjection.Flatten(FleetBoardWings))
        {
            FleetBoardRows.Add(row);
        }
    }

    private void OnLiveFleetMemberPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.PropertyName == nameof(LiveFleetBoardMemberViewModel.IsSelected))
        {
            RefreshLiveSelectionSummary();
        }
    }

    private void RefreshLiveSelectionSummary()
    {
        OnPropertyChanged(nameof(LiveFleetSelectedCount));
        OnPropertyChanged(nameof(LiveFleetVisibleCount));
        OnPropertyChanged(nameof(LiveFleetSelectionSummary));
        OnPropertyChanged(nameof(LiveFleetSearchSummary));
        RefreshLiveBulkMoveTargets();
    }

    private void RefreshPendingLiveChangeState()
    {
        OnPropertyChanged(nameof(HasStagedLiveMoves));
        OnPropertyChanged(nameof(HasStagedLiveInvites));
        OnPropertyChanged(nameof(HasStagedLiveStructureChanges));
        OnPropertyChanged(nameof(HasPendingLiveChanges));
        OnPropertyChanged(nameof(PendingLiveChangeCount));
        OnPropertyChanged(nameof(StagedLiveMovesSummary));
        OnPropertyChanged(nameof(PendingLiveChangesSummary));
        OnPropertyChanged(nameof(ApplyPendingLiveChangesText));
        OnPropertyChanged(nameof(CanApplyPendingLiveChanges));
        OnPropertyChanged(nameof(SentLiveInvitesSummary));
        if (currentSnapshot is not null)
        {
            BuildFleetBoard(currentSnapshot);
        }
    }

    private static List<LiveFleetTreeNodeViewModel> BuildFleetTree(LiveFleetSnapshot snapshot)
    {
        var roots = new List<LiveFleetTreeNodeViewModel>();
        var placedCharacterIds = new HashSet<long>();

        var commandMembers = SortMembers(snapshot.Members.Where(member => member.WingId < 0).ToArray());
        if (commandMembers.Length > 0)
        {
            foreach (var member in commandMembers)
            {
                placedCharacterIds.Add(member.CharacterId);
            }

            roots.Add(new LiveFleetTreeNodeViewModel(
                "Fleet Command",
                MemberCountText(commandMembers.Length),
                commandMembers.Select(CreateMemberNode).ToArray()));
        }

        foreach (var wing in snapshot.Wings)
        {
            var wingChildren = new List<LiveFleetTreeNodeViewModel>();
            var wingCommanders = SortMembers(snapshot.Members
                .Where(member => member.WingId == wing.WingId && member.SquadId < 0)
                .ToArray());
            foreach (var commander in wingCommanders)
            {
                placedCharacterIds.Add(commander.CharacterId);
                wingChildren.Add(CreateMemberNode(commander));
            }

            foreach (var squad in wing.Squads)
            {
                var squadMembers = SortMembers(snapshot.Members
                    .Where(member => member.WingId == wing.WingId && member.SquadId == squad.SquadId)
                    .ToArray());
                foreach (var member in squadMembers)
                {
                    placedCharacterIds.Add(member.CharacterId);
                }

                wingChildren.Add(new LiveFleetTreeNodeViewModel(
                    squad.Name,
                    MemberCountText(squadMembers.Length),
                    squadMembers.Select(CreateMemberNode).ToArray()));
            }

            var wingMemberCount = snapshot.Members.Count(member => member.WingId == wing.WingId);
            roots.Add(new LiveFleetTreeNodeViewModel(
                wing.Name,
                MemberCountText(wingMemberCount),
                wingChildren.ToArray()));
        }

        var unmatchedMembers = SortMembers(snapshot.Members
            .Where(member => !placedCharacterIds.Contains(member.CharacterId))
            .ToArray());
        if (unmatchedMembers.Length > 0)
        {
            roots.Add(new LiveFleetTreeNodeViewModel(
                "Unassigned / unknown structure",
                MemberCountText(unmatchedMembers.Length),
                unmatchedMembers.Select(CreateMemberNode).ToArray()));
        }

        return roots;
    }

    private static LiveFleetMember[] SortMembers(LiveFleetMember[] members) =>
        members
            .OrderBy(member => RoleOrder(member.Role))
            .ThenBy(member => member.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static LiveFleetTreeNodeViewModel CreateMemberNode(LiveFleetMember member)
    {
        var location = member.StationName ?? member.SolarSystemName;
        return new LiveFleetTreeNodeViewModel(
            member.CharacterName,
            $"{member.RoleName} • {member.ShipTypeName} • {location} • fleet warp {(member.TakesFleetWarp ? "on" : "off")}",
            []);
    }

    private static int RoleOrder(string role) => role switch
    {
        "fleet_commander" => 0,
        "wing_commander" => 1,
        "squad_commander" => 2,
        _ => 3,
    };

    private static string MemberCountText(int count) =>
        $"{count} member{(count == 1 ? string.Empty : "s")}";}

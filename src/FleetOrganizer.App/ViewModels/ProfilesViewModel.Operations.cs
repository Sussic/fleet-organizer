using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetOrganizer.App.Services;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;
using FleetOrganizer.Core.Planning;
using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.App.ViewModels;

public partial class ProfilesViewModel
{
    [RelayCommand]
    private void HideDryRun() => InvalidateDryRun();

    [RelayCommand]
    private Task<bool> StartOperationAsync() => StartPreparedOperationAsync();

    public async Task<bool> StartPreparedOperationAsync()
    {
        var reviewedPlan = lastDryRunPlan;
        var preparedProfile = lastPreparedProfile;
        if (reviewedPlan is null || preparedProfile is null)
        {
            StatusMessage = "Generate and review a current dry run before starting.";
            return false;
        }

        if (!reviewedPlan.CanExecute)
        {
            StatusMessage = string.IsNullOrWhiteSpace(DryRunBlockingDetails)
                ? "Resolve the dry-run safety blockers before starting."
                : DryRunBlockingDetails;
            return false;
        }

        if (!CanStartReviewedOperation)
        {
            StatusMessage = reviewedPlan.TotalChanges == 0
                ? "The live fleet already matches. There is nothing to send."
                : "Finish the current fleet operation before starting another one.";
            return false;
        }

        var actionCount = reviewedPlan.TotalChanges;
        var isRoutineLiveChange = reviewedPlan.Mode == FleetRunMode.ApplyLiveChanges;
        if (!userInteraction.Confirm(
            isRoutineLiveChange ? "Apply fleet changes" : "Start fleet setup",
            $"{(isRoutineLiveChange ? "Apply" : "Start")} {actionCount} reviewed fleet change{(actionCount == 1 ? string.Empty : "s")}?\n\n" +
            $"Setup: {reviewedPlan.ProfileName}\n" +
            $"Mode: {HumanizeRunMode(reviewedPlan.Mode)}\n" +
            $"Fleet: {reviewedPlan.FleetId}\n" +
            $"Structure creates: {reviewedPlan.StructureCreates}\n" +
            $"Structure renames: {reviewedPlan.StructureRenames}\n" +
            $"Invitations: {reviewedPlan.CharacterInvites}\n" +
            $"Moves and role changes: {reviewedPlan.CharacterMoves + reviewedPlan.RoleChanges}\n\n" +
            "Fleet Desk re-checks the fleet and fleet-boss authority before writing. Nothing outside this reviewed list is changed.",
            UserConfirmationKind.Question))
        {
            StatusMessage = "No fleet changes were sent.";
            return false;
        }

        IsBusy = true;
        StatusMessage = "Re-checking the reviewed plan before the first write…";
        try
        {
            var result = await operationService.StartAsync(
                preparedProfile,
                reviewedPlan);
            ApplyDryRun(result.CurrentPlan, DateTimeOffset.UtcNow);
            if (!result.Started || result.Operation is null)
            {
                StatusMessage = result.UserMessage;
                return false;
            }

            ShowOperation(result.Operation);
            await ReloadOperationHistoryAsync();
            StatusMessage = result.UserMessage;
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Operation could not start: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ContinueOperationAsync()
    {
        var operation = currentOperation;
        if (!CanOperate || operation is null || operation.IsTerminal)
        {
            return;
        }

        RaiseInvitationTimeoutAttention(operation);

        await RunOperationActionAsync(
            () => operationService.ContinueAsync(operation.Id),
            "Checking accepted invitations and current placements…");
    }

    public async Task AutoContinueOperationAsync()
    {
        var operation = currentOperation;
        if (IsBusy ||
            operation is null ||
            operation.IsTerminal ||
            operation.State != OperationState.AwaitAcceptance)
        {
            return;
        }

        RaiseInvitationTimeoutAttention(operation);
        await RunOperationActionAsync(
            () => operationService.ContinueAsync(operation.Id),
            "Automatically checking for accepted invitations…");
    }

    private void RaiseInvitationTimeoutAttention(FleetOperation operation)
    {
        if (inviteTimeoutRaisedForOperation == operation.Id)
        {
            return;
        }

        var waitingSince = operation.Steps
            .Where(step => step.Type == FleetOperationStepType.Invite &&
                step.State == FleetOperationStepState.Waiting)
            .Select(step => step.UpdatedAtUtc)
            .DefaultIfEmpty(operation.CreatedAtUtc)
            .Min();
        if (DateTimeOffset.UtcNow - waitingSince < TimeSpan.FromMinutes(InvitationTimeoutMinutes))
        {
            return;
        }

        inviteTimeoutRaisedForOperation = operation.Id;
        AttentionRequested?.Invoke(
            this,
            new FleetAttentionEventArgs(
                $"Invitations have been waiting for {InvitationTimeoutMinutes} minutes. Automatic safe checks will continue; inspect the listed clients when convenient.",
                isUrgent: true));
    }

    [RelayCommand]
    private async Task RetryOperationStepAsync(FleetOperationStepViewModel? item)
    {
        var operation = currentOperation;
        if (!CanOperate || operation is null || item is null || !item.CanRetry)
        {
            return;
        }

        await RunOperationActionAsync(
            () => operationService.RetryStepAsync(operation.Id, item.StepKey),
            $"Re-checking {item.Title} before retry…");
    }

    [RelayCommand]
    private async Task SkipOperationStepAsync(FleetOperationStepViewModel? item)
    {
        var operation = currentOperation;
        if (!CanOperate || operation is null || item is null || !item.CanSkip)
        {
            return;
        }

        if (!userInteraction.Confirm(
            "Skip operation step",
            $"Skip this operation step?\n\n{item.Title}\n\nNo further write will be sent for it during this run.",
            UserConfirmationKind.Warning))
        {
            return;
        }

        await RunOperationActionAsync(
            () => operationService.SkipStepAsync(operation.Id, item.StepKey),
            $"Skipping {item.Title}…");
    }

    [RelayCommand]
    private async Task CancelOperationAsync()
    {
        var operation = currentOperation;
        if (!CanOperate || operation is null || operation.IsTerminal)
        {
            return;
        }

        if (!userInteraction.Confirm(
            "Cancel operation",
            "Cancel this saved operation?\n\nAlready accepted ESI writes are not undone. Pending steps will stop and the run will not resume automatically.",
            UserConfirmationKind.Warning))
        {
            return;
        }

        await RunOperationActionAsync(
            () => operationService.CancelAsync(
                operation.Id,
                "Cancelled by the user. Already accepted writes were not undone."),
            "Cancelling the saved operation…");
    }

    [RelayCommand]
    private void HideOperation()
    {
        if (currentOperation is not null && !currentOperation.IsTerminal)
        {
            StatusMessage = "Complete or cancel the active operation before hiding it.";
            return;
        }

        currentOperation = null;
        OperationItems.Clear();
        HasOperation = false;
        HasActiveOperation = false;
        OperationIsTerminal = false;
        OperationTitle = "No saved operation.";
        OperationSummary = string.Empty;
        OperationStatusMessage = string.Empty;
        OperationPhaseTitle = "No fleet run is active";
        OperationNextAction = "Choose a saved setup on Live Fleet to prepare a run.";
        OperationProgressText = "0 of 0 steps finished";
        OperationProgressPercent = 0;
        WaitingCharacters.Clear();
        IsWaitingForInvites = false;
        WaitingRoomSummary = "No invitations are waiting.";
        HasRestoreSnapshot = false;
    }

    [RelayCommand]
    private async Task OpenHistoryOperationAsync(FleetOperationHistoryItemViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var operation = await operationService.LoadAsync(item.Id);
            if (operation is null)
            {
                StatusMessage = "That saved run is no longer available.";
                await ReloadOperationHistoryAsync();
                return;
            }

            ShowOperation(operation);
            StatusMessage = $"Opened saved run from {operation.UpdatedAtUtc.ToLocalTime():g}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> PrepareRestorePreviewAsync()
    {
        var operation = currentOperation;
        if (operation is null || !operation.IsTerminal || IsBusy)
        {
            StatusMessage = "Finish the current run before preparing a restore preview.";
            return false;
        }

        IsBusy = true;
        StatusMessage = "Loading the pre-run snapshot and comparing it with the live fleet…";
        try
        {
            var initialSnapshot = await operationService.LoadInitialSnapshotAsync(operation.Id);
            if (initialSnapshot is null)
            {
                HasRestoreSnapshot = false;
                StatusMessage = "This run does not have a pre-run snapshot to preview.";
                return false;
            }

            var liveResult = await liveFleetService.LoadCurrentAsync();
            if (liveResult.Status != LiveFleetLoadStatus.Ready || liveResult.Snapshot is null)
            {
                StatusMessage = liveResult.UserMessage;
                return false;
            }

            var restoreProfile = FleetProfileFactory.FromLiveFleet(
                initialSnapshot,
                $"Restore before {operation.ProfileName}") with
            {
                Id = operation.ProfileId,
            };
            lastPreparedProfile = restoreProfile;
            lastShipRuleMatchCount = 0;
            lastShipRuleCapacitySkipCount = 0;
            var plan = FleetPlanModeFilter.Apply(
                FleetPlanner.Build(restoreProfile, liveResult.Snapshot),
                FleetRunMode.FullOrganise);
            ApplyDryRun(plan, liveResult.Snapshot.ConfirmedAtUtc);
            DryRunSafetyMessage +=
                " Restore is best-effort: the preview never kicks members or deletes hierarchy, and characters who left may appear as invitations.";
            StatusMessage = "Restore preview ready. Review every proposed change before starting it.";
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Restore preview could not be prepared: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<bool> PrepareStagedMovesAsync(
        LiveFleetSnapshot snapshot,
        StagedLiveMoveViewModel[] stagedMoves) =>
        PrepareLiveDeskChangesAsync(snapshot, stagedMoves, [], []);

    public async Task<bool> PrepareLiveDeskChangesAsync(
        LiveFleetSnapshot snapshot,
        StagedLiveMoveViewModel[] stagedMoves,
        StagedLiveInviteViewModel[] stagedInvites,
        StagedLiveStructureChangeViewModel[] stagedStructureChanges)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(stagedMoves);
        ArgumentNullException.ThrowIfNull(stagedInvites);
        ArgumentNullException.ThrowIfNull(stagedStructureChanges);

        if (stagedMoves.Length == 0 && stagedInvites.Length == 0 && stagedStructureChanges.Length == 0)
        {
            StatusMessage = "Stage at least one live-fleet change first.";
            return false;
        }

        var prepared = await liveFleetRunCoordinator.PrepareAsync(
            snapshot,
            stagedMoves,
            stagedInvites,
            stagedStructureChanges);
        lastPreparedProfile = prepared.Profile;
        lastShipRuleMatchCount = 0;
        lastShipRuleCapacitySkipCount = 0;
        ApplyDryRun(prepared.Plan, snapshot.ConfirmedAtUtc);
        var changeCount = prepared.RequestedChangeCount;
        DryRunTitle = $"{changeCount} staged Live Desk change{(changeCount == 1 ? string.Empty : "s")} • fleet {snapshot.FleetId}";
        DryRunSafetyMessage +=
            " This run contains only the moves, roles, invitations, and hierarchy edits staged on Live Fleet. Fleet-boss transfer, kicks, and deletion use their separate high-impact unlock.";
        StatusMessage = "Live Desk preview ready. One final confirmation remains before any ESI write.";
        return true;
    }

    private void InvalidateDryRun()
    {
        lastDryRunPlan = null;
        lastPreparedProfile = null;
        lastShipRuleMatchCount = 0;
        lastShipRuleCapacitySkipCount = 0;
        DryRunItems.Clear();
        HasDryRun = false;
        DryRunTitle = "No comparison generated.";
        DryRunSummary = string.Empty;
        DryRunSafetyMessage = string.Empty;
        DryRunPrimaryMessage = "Choose a profile and check the live fleet.";
        DryRunBlockingDetails = string.Empty;
        ShipRuleMatchSummary = string.Empty;
        OnPropertyChanged(nameof(CanStartReviewedOperation));
        OnPropertyChanged(nameof(RunPrimaryActionText));
    }

    private void ApplyDryRun(FleetDryRunPlan plan, DateTimeOffset confirmedAtUtc)
    {
        lastDryRunPlan = plan;
        DryRunTitle =
            $"'{plan.ProfileName}' compared with fleet {plan.FleetId} • live state confirmed {confirmedAtUtc.ToLocalTime():t}";
        DryRunSummary = BuildDryRunSummary(plan);
        DryRunSafetyMessage = BuildDryRunSafetyMessage(plan);
        ShipRuleMatchSummary = ShipRules.Count == 0
            ? "No automatic ship placement rules are enabled."
            : lastShipRuleMatchCount == 0 && lastShipRuleCapacitySkipCount == 0
                ? "No current live characters matched the saved ship rules."
                : $"{lastShipRuleMatchCount} live character{(lastShipRuleMatchCount == 1 ? string.Empty : "s")} matched by ship policy" +
                    (lastShipRuleCapacitySkipCount == 0
                        ? "."
                        : $" • {lastShipRuleCapacitySkipCount} left untouched because every configured target was full.");
        DryRunPrimaryMessage = plan.BlockingIssues > 0
            ? "This run needs attention before it can start"
            : plan.TotalChanges == 0
                ? "Fleet is already organised"
                : "Ready for your confirmation";
        DryRunBlockingDetails = string.Join(
            Environment.NewLine,
            plan.Items
                .Where(item => item.Kind == FleetPlanItemKind.Blocked)
                .Take(3)
                .Select(item => $"{item.Title}: {item.Detail}"));
        HasDryRun = true;
        RefreshVisibleDryRunItems();
        OnPropertyChanged(nameof(CanStartReviewedOperation));
        OnPropertyChanged(nameof(RunPrimaryActionText));
    }

    private async Task RunOperationActionAsync(
        Func<Task<FleetOperation>> action,
        string progressMessage)
    {
        IsBusy = true;
        StatusMessage = progressMessage;
        try
        {
            var operation = await action();
            ShowOperation(operation);
            await ReloadOperationHistoryAsync();
            StatusMessage = operation.Message ?? "Operation updated.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Operation could not continue: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadOperationHistoryAsync()
    {
        var operations = await operationService.LoadRecentAsync();
        OperationHistory.Clear();
        foreach (var operation in operations)
        {
            OperationHistory.Add(new FleetOperationHistoryItemViewModel(operation));
        }
    }

    private void ShowOperation(FleetOperation operation)
    {
        var previousState = currentOperation?.State;
        if (currentOperation?.Id != operation.Id)
        {
            inviteTimeoutRaisedForOperation = null;
        }

        currentOperation = operation;
        OperationItems.Clear();
        foreach (var step in operation.Steps.OrderBy(step => step.SortOrder))
        {
            OperationItems.Add(new FleetOperationStepViewModel(step));
        }

        HasOperation = true;
        HasActiveOperation = !operation.IsTerminal;
        OperationIsTerminal = operation.IsTerminal;
        OperationTitle =
            $"{operation.ProfileName} • fleet {operation.FleetId} • {HumanizeOperationState(operation.State)}";
        OperationSummary =
            $"{operation.SucceededSteps} confirmed • {operation.WaitingSteps} waiting • " +
            $"{operation.PendingSteps} pending • {operation.FailedSteps} failed • {operation.SkippedSteps} skipped";
        OperationStatusMessage = operation.Message ?? string.Empty;
        OperationPhaseTitle = GetOperationPhaseTitle(operation.State);
        OperationNextAction = GetOperationNextAction(operation.State);
        var finishedSteps = operation.SucceededSteps + operation.FailedSteps + operation.SkippedSteps;
        var totalSteps = operation.Steps.Length;
        OperationProgressText =
            $"{finishedSteps} of {totalSteps} steps finished";
        OperationProgressPercent = totalSteps == 0
            ? operation.IsTerminal ? 100 : 0
            : Math.Clamp((double)finishedSteps / totalSteps * 100, 0, 100);

        WaitingCharacters.Clear();
        if (operation.State == OperationState.AwaitAcceptance)
        {
            var waitingIds = operation.Steps
                .Where(step => step.Type == FleetOperationStepType.Place &&
                    step.State == FleetOperationStepState.Waiting)
                .Select(step => step.Target.CharacterId)
                .ToHashSet();
            foreach (var invite in operation.Steps
                .Where(step => step.Type == FleetOperationStepType.Invite &&
                    waitingIds.Contains(step.Target.CharacterId))
                .OrderBy(step => step.Target.CharacterName, StringComparer.OrdinalIgnoreCase))
            {
                WaitingCharacters.Add(new WaitingCharacterViewModel(
                    invite.Target.CharacterId,
                    invite.Target.CharacterName,
                    $"{invite.Target.WingName} / {invite.Target.SquadName}",
                    "Invite sent — waiting in EVE"));
            }
        }

        IsWaitingForInvites = WaitingCharacters.Count > 0;
        WaitingRoomSummary = IsWaitingForInvites
            ? $"Waiting for {WaitingCharacters.Count} character{(WaitingCharacters.Count == 1 ? string.Empty : "s")} to accept in EVE."
            : "No invitations are waiting.";
        HasRestoreSnapshot = operation.IsTerminal;

        if (previousState == OperationState.AwaitAcceptance &&
            operation.State != OperationState.AwaitAcceptance)
        {
            AttentionRequested?.Invoke(
                this,
                new FleetAttentionEventArgs("All accepted characters are ready for the next fleet step.", false));
        }

        if (operation.State == OperationState.NeedsAttention &&
            previousState != OperationState.NeedsAttention)
        {
            AttentionRequested?.Invoke(
                this,
                new FleetAttentionEventArgs("Fleet run needs attention before it can continue.", true));
        }
        else if (operation.State == OperationState.Complete &&
            previousState != OperationState.Complete)
        {
            AttentionRequested?.Invoke(
                this,
                new FleetAttentionEventArgs("Fleet organisation is complete and verified.", false));
        }
    }

    private static string GetOperationPhaseTitle(OperationState state) => state switch
    {
        OperationState.EnsureStructure => "1. Preparing wings and squads",
        OperationState.InviteMissing => "2. Sending invitations",
        OperationState.AwaitAcceptance => "3. Waiting for characters to accept",
        OperationState.PlaceMembers => "4. Placing characters",
        OperationState.AssignCommanders => "5. Assigning commanders",
        OperationState.Verify => "6. Checking the finished fleet",
        OperationState.NeedsAttention => "Action needed",
        OperationState.Complete => "Fleet ready",
        OperationState.Cancelled => "Run cancelled safely",
        _ => "Preparing the fleet run",
    };

    private static string GetOperationNextAction(OperationState state) => state switch
    {
        OperationState.AwaitAcceptance =>
            "Accept the invitations in EVE. Fleet Desk checks every 30 seconds while open; use Check now if you do not want to wait.",
        OperationState.NeedsAttention =>
            "Open the technical steps below, then retry or skip the failed item.",
        OperationState.Complete =>
            "The requested layout was confirmed against the live fleet.",
        OperationState.Cancelled =>
            "No more writes will be sent. Writes already accepted by EVE were left in place.",
        _ => "Fleet Desk will perform one guarded step at a time.",
    };

    private static string HumanizeOperationState(OperationState state) => state switch
    {
        OperationState.InviteMissing => "inviting",
        OperationState.EnsureStructure => "repairing structure",
        OperationState.AwaitAcceptance => "awaiting acceptance",
        OperationState.PlaceMembers => "placing members",
        OperationState.AssignCommanders => "assigning commanders",
        OperationState.Verify => "verifying",
        OperationState.NeedsAttention => "needs attention",
        OperationState.Complete => "complete",
        OperationState.Cancelled => "cancelled",
        _ => state.ToString(),
    };

    private void RefreshVisibleDryRunItems()
    {
        DryRunItems.Clear();
        if (lastDryRunPlan is null)
        {
            return;
        }

        foreach (var item in lastDryRunPlan.Items)
        {
            var viewModel = new FleetPlanItemViewModel(item);
            if (ShowAlreadyCorrect || !viewModel.IsAlreadyCorrect)
            {
                DryRunItems.Add(viewModel);
            }
        }
    }

    private static string BuildDryRunSummary(FleetDryRunPlan plan) =>
        $"{HumanizeRunMode(plan.Mode)} • {plan.StructureChanges} structure • {plan.CharacterInvites} invites • " +
        $"{plan.CharacterMoves} moves • {plan.RoleChanges} role changes • " +
        $"{plan.AlreadyCorrect} already correct • {plan.IgnoredLiveMembers} live members left untouched";

    private static string BuildDryRunSafetyMessage(FleetDryRunPlan plan)
    {
        if (plan.BlockingIssues > 0)
        {
            return $"Blocked: resolve {plan.BlockingIssues} safety issue{(plan.BlockingIssues == 1 ? string.Empty : "s")} before an operation could start.";
        }

        var baseMessage = plan.TotalChanges == 0
            ? "The saved assignments already match the live fleet. No changes are needed."
            : "Only the actions shown for this run mode are eligible after one final confirmation. No hierarchy is deleted, nobody is kicked, and fleet boss is never transferred.";
        return plan.IgnoredLiveMembers == 0
            ? baseMessage
            : $"{baseMessage} {plan.IgnoredLiveMembers} unmanaged live member{(plan.IgnoredLiveMembers == 1 ? " is" : "s are")} intentionally left untouched.";
    }

    private static string HumanizeRunMode(FleetRunMode mode) => mode switch
    {
        FleetRunMode.FullOrganise => "Full organise",
        FleetRunMode.InviteMissing => "Invite missing",
        FleetRunMode.PlacePresent => "Place joined",
        FleetRunMode.FixStructure => "Fix structure",
        FleetRunMode.AssignCommanders => "Assign commanders",
        FleetRunMode.ApplyLiveChanges => "Live changes",
        _ => mode.ToString(),
    };

    private static string GetSafeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var safeCharacters = value
            .Select(character => invalidCharacters.Contains(character) ? '-' : character)
            .ToArray();
        var safeName = new string(safeCharacters).Trim();
        return safeName.Length == 0 ? "fleet-profile" : safeName;
    }}

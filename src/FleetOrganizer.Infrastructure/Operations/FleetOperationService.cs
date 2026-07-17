using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;
using FleetOrganizer.Core.Planning;

namespace FleetOrganizer.Infrastructure.Operations;

internal sealed class FleetOperationService : IFleetOperationService, IDisposable
{
    private readonly IFleetOperationRepository repository;
    private readonly ILiveFleetService liveFleetService;
    private readonly IFleetWriteService writeService;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim operationGate = new(1, 1);

    public FleetOperationService(
        IFleetOperationRepository repository,
        ILiveFleetService liveFleetService,
        IFleetWriteService writeService,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(liveFleetService);
        ArgumentNullException.ThrowIfNull(writeService);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.repository = repository;
        this.liveFleetService = liveFleetService;
        this.writeService = writeService;
        this.timeProvider = timeProvider;
    }

    public Task<FleetOperation?> LoadLatestResumableAsync(
        CancellationToken cancellationToken = default) =>
        repository.LoadLatestResumableAsync(cancellationToken);

    public async Task<FleetOperationStartResult> StartAsync(
        FleetProfile profile,
        FleetDryRunPlan reviewedPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await repository
                .LoadLatestResumableAsync(cancellationToken)
                .ConfigureAwait(false) is not null)
            {
                throw new InvalidOperationException(
                    "Resume or cancel the saved operation before starting another one.");
            }

            var liveResult = await liveFleetService
                .RefreshCurrentAsync(cancellationToken)
                .ConfigureAwait(false);
            var snapshot = RequireReadySnapshot(liveResult);
            var currentPlan = FleetPlanner.Build(profile, snapshot);
            if (!string.Equals(
                FleetOperationFactory.GetReviewSignature(reviewedPlan),
                FleetOperationFactory.GetReviewSignature(currentPlan),
                StringComparison.Ordinal))
            {
                return new FleetOperationStartResult(
                    Started: false,
                    Operation: null,
                    currentPlan,
                    "The live fleet changed after the dry run. Review the refreshed comparison, then start again.");
            }

            var now = timeProvider.GetUtcNow();
            var operation = FleetOperationFactory.Create(
                Guid.NewGuid(),
                profile,
                snapshot,
                currentPlan,
                now);
            await repository
                .SaveAsync(operation, snapshot, cancellationToken)
                .ConfigureAwait(false);
            operation = await RunCoreAsync(operation, snapshot, cancellationToken)
                .ConfigureAwait(false);
            return new FleetOperationStartResult(
                Started: true,
                operation,
                currentPlan,
                operation.Message ?? "The operation started.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<FleetOperation> ContinueAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var operation = await RequireOperationAsync(operationId, cancellationToken)
                .ConfigureAwait(false);
            if (operation.IsTerminal)
            {
                return operation;
            }

            var liveResult = await liveFleetService
                .RefreshCurrentAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!TryValidateSnapshot(operation, liveResult, out var snapshot, out var failureMessage))
            {
                return await SaveOperationAsync(
                    operation with
                    {
                        State = OperationState.NeedsAttention,
                        Message = failureMessage,
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            return await RunCoreAsync(operation, snapshot!, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<FleetOperation> RetryStepAsync(
        Guid operationId,
        string stepKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepKey);

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var operation = await RequireOperationAsync(operationId, cancellationToken)
                .ConfigureAwait(false);
            var step = operation.Steps.Single(candidate =>
                string.Equals(candidate.StepKey, stepKey, StringComparison.Ordinal));
            if (step.State != FleetOperationStepState.Failed)
            {
                throw new InvalidOperationException("Only a failed step can be retried explicitly.");
            }

            var now = timeProvider.GetUtcNow();
            operation = ReplaceStep(
                operation,
                step with
                {
                    State = FleetOperationStepState.Pending,
                    LastFailureKind = null,
                    Message = "Retry approved. The live fleet will be checked before another write.",
                    RetryAfterUtc = null,
                    UpdatedAtUtc = now,
                },
                now);
            await repository.SaveAsync(operation, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var liveResult = await liveFleetService
                .RefreshCurrentAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!TryValidateSnapshot(operation, liveResult, out var snapshot, out var failureMessage))
            {
                return await SaveOperationAsync(
                    operation with
                    {
                        State = OperationState.NeedsAttention,
                        Message = failureMessage,
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            return await RunCoreAsync(operation, snapshot!, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<FleetOperation> SkipStepAsync(
        Guid operationId,
        string stepKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepKey);

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var operation = await RequireOperationAsync(operationId, cancellationToken)
                .ConfigureAwait(false);
            var step = operation.Steps.Single(candidate =>
                string.Equals(candidate.StepKey, stepKey, StringComparison.Ordinal));
            if (step.State is FleetOperationStepState.Succeeded or FleetOperationStepState.Skipped)
            {
                return operation;
            }

            var now = timeProvider.GetUtcNow();
            operation = ReplaceStep(
                operation,
                step with
                {
                    State = FleetOperationStepState.Skipped,
                    LastFailureKind = null,
                    Message = "Skipped by the user. No additional write will be sent for this step.",
                    RetryAfterUtc = null,
                    UpdatedAtUtc = now,
                },
                now);
            operation = FinalizeState(operation, now);
            return await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<FleetOperation> CancelAsync(
        Guid operationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var operation = await RequireOperationAsync(operationId, cancellationToken)
                .ConfigureAwait(false);
            if (operation.IsTerminal)
            {
                return operation;
            }

            return await SaveOperationAsync(
                operation with
                {
                    State = OperationState.Cancelled,
                    Message = reason.Trim(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public void Dispose()
    {
        operationGate.Dispose();
    }

    private async Task<FleetOperation> RunCoreAsync(
        FleetOperation operation,
        LiveFleetSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var initialSnapshot = await repository
            .LoadInitialSnapshotAsync(operation.Id, cancellationToken)
            .ConfigureAwait(false) ?? snapshot;
        var now = timeProvider.GetUtcNow();
        operation = ReconcileWithSnapshot(operation, snapshot, initialSnapshot, now) with
        {
            State = OperationState.ReadCurrentState,
            Message = "Live fleet and fleet-boss authority confirmed.",
        };
        operation = await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);

        operation = await RunStructurePhaseAsync(
            operation,
            snapshot,
            initialSnapshot,
            cancellationToken).ConfigureAwait(false);
        if (HasUnfinishedSteps(operation, IsStructureStep))
        {
            operation = FinalizeState(operation, timeProvider.GetUtcNow());
            return await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);
        }

        if (operation.Steps.Any(step =>
            (step.Type is
                FleetOperationStepType.Invite or
                FleetOperationStepType.Place or
                FleetOperationStepType.PromoteCommander) &&
            (step.Target.WingId <= 0 || step.Target.SquadId <= 0)))
        {
            operation = operation with
            {
                State = OperationState.NeedsAttention,
                Message = "A character target has no confirmed wing or squad ID. No roster write was sent.",
            };
            return await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);
        }

        var inviteWritesSent = false;
        var writePauseObserved = false;
        foreach (var step in operation.Steps
            .Where(step =>
                step.Type == FleetOperationStepType.Invite &&
                step.State == FleetOperationStepState.Pending)
            .OrderBy(step => step.SortOrder))
        {
            now = timeProvider.GetUtcNow();
            if (step.RetryAfterUtc is DateTimeOffset retryAfter && retryAfter > now)
            {
                continue;
            }

            operation = await MarkRunningAsync(operation, step, cancellationToken)
                .ConfigureAwait(false);
            var runningStep = operation.Steps.Single(candidate =>
                string.Equals(candidate.StepKey, step.StepKey, StringComparison.Ordinal));
            var result = await writeService
                .InviteAsync(operation.FleetId, runningStep.Target, cancellationToken)
                .ConfigureAwait(false);
            inviteWritesSent |= result.IsSuccess;
            operation = await ApplyWriteResultAsync(
                operation,
                runningStep,
                result,
                successMessage: "Invitation sent. Waiting for the character to accept in EVE.",
                cancellationToken).ConfigureAwait(false);
            if (MustStopWritePass(result.FailureKind))
            {
                writePauseObserved = true;
                break;
            }
        }

        if (inviteWritesSent && !writePauseObserved)
        {
            var refresh = await liveFleetService
                .RefreshCurrentAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!TryValidateSnapshot(operation, refresh, out var refreshedSnapshot, out var failure))
            {
                return await SaveOperationAsync(
                    operation with
                    {
                        State = OperationState.NeedsAttention,
                        Message = failure,
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            snapshot = refreshedSnapshot!;
            operation = ReconcileWithSnapshot(
                operation,
                snapshot,
                initialSnapshot,
                timeProvider.GetUtcNow());
            operation = await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);
        }

        if (writePauseObserved)
        {
            operation = FinalizeState(operation, timeProvider.GetUtcNow());
            return await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);
        }

        var placementWritesSent = false;
        foreach (var step in operation.Steps
            .Where(step =>
                step.Type == FleetOperationStepType.Place &&
                step.State == FleetOperationStepState.Pending)
            .OrderBy(step => step.SortOrder))
        {
            now = timeProvider.GetUtcNow();
            if (step.RetryAfterUtc is DateTimeOffset retryAfter && retryAfter > now)
            {
                continue;
            }

            operation = await MarkRunningAsync(operation, step, cancellationToken)
                .ConfigureAwait(false);
            var runningStep = operation.Steps.Single(candidate =>
                string.Equals(candidate.StepKey, step.StepKey, StringComparison.Ordinal));
            var result = await writeService
                .PlaceAsync(operation.FleetId, runningStep.Target, cancellationToken)
                .ConfigureAwait(false);
            placementWritesSent |= result.IsSuccess;
            operation = await ApplyWriteResultAsync(
                operation,
                runningStep,
                result,
                successMessage: "Placement sent. Waiting for ESI to confirm the member position.",
                cancellationToken).ConfigureAwait(false);
            if (MustStopWritePass(result.FailureKind))
            {
                writePauseObserved = true;
                break;
            }
        }

        if (placementWritesSent && !writePauseObserved)
        {
            var refresh = await liveFleetService
                .RefreshCurrentAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!TryValidateSnapshot(operation, refresh, out var refreshedSnapshot, out var failure))
            {
                return await SaveOperationAsync(
                    operation with
                    {
                        State = OperationState.NeedsAttention,
                        Message = failure,
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            operation = ReconcileWithSnapshot(
                operation,
                refreshedSnapshot!,
                initialSnapshot,
                timeProvider.GetUtcNow());
        }

        if (writePauseObserved)
        {
            operation = FinalizeState(operation, timeProvider.GetUtcNow());
            return await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);
        }

        if (!HasUnfinishedSteps(operation, step => step.Type == FleetOperationStepType.Place))
        {
            operation = await RunCommanderPhaseAsync(
                operation,
                initialSnapshot,
                cancellationToken).ConfigureAwait(false);
        }

        operation = await SaveOperationAsync(
            operation with
            {
                State = OperationState.Verify,
                Message = "Running a final live hierarchy, placement, and role verification.",
            },
            cancellationToken).ConfigureAwait(false);
        var finalRefresh = await liveFleetService
            .RefreshCurrentAsync(cancellationToken)
            .ConfigureAwait(false);
        if (TryValidateSnapshot(operation, finalRefresh, out var finalSnapshot, out var finalFailure))
        {
            operation = ReconcileWithSnapshot(
                operation,
                finalSnapshot!,
                initialSnapshot,
                timeProvider.GetUtcNow());
        }
        else
        {
            operation = operation with
            {
                State = OperationState.NeedsAttention,
                Message = finalFailure,
            };
            return await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);
        }

        operation = FinalizeState(operation, timeProvider.GetUtcNow());
        return await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FleetOperation> RunStructurePhaseAsync(
        FleetOperation operation,
        LiveFleetSnapshot snapshot,
        LiveFleetSnapshot initialSnapshot,
        CancellationToken cancellationToken)
    {
        var wroteStructure = false;
        while (true)
        {
            operation = ActivateReadyStructureSteps(operation, timeProvider.GetUtcNow());
            var now = timeProvider.GetUtcNow();
            var step = operation.Steps
                .Where(candidate =>
                    IsStructureStep(candidate) &&
                    candidate.State == FleetOperationStepState.Pending &&
                    (candidate.RetryAfterUtc is null || candidate.RetryAfterUtc <= now))
                .OrderBy(candidate => candidate.SortOrder)
                .FirstOrDefault();
            if (step is null)
            {
                break;
            }

            operation = await MarkRunningAsync(operation, step, cancellationToken)
                .ConfigureAwait(false);
            var runningStep = FindStep(operation, step.StepKey);
            var result = await ExecuteStructureWriteAsync(
                operation.FleetId,
                runningStep,
                cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess &&
                (runningStep.Type is FleetOperationStepType.CreateWing or
                    FleetOperationStepType.CreateSquad))
            {
                if (result.CreatedId is not > 0)
                {
                    result = new FleetWriteResult(
                        IsSuccess: false,
                        FleetWriteFailureKind.InvalidResponse,
                        "ESI accepted the create request but did not return the new structure ID.",
                        result.RequestId,
                        RetryAfter: null);
                }
                else
                {
                    operation = PropagateCreatedId(
                        operation,
                        runningStep,
                        result.CreatedId.Value,
                        timeProvider.GetUtcNow());
                    runningStep = FindStep(operation, step.StepKey);
                }
            }

            wroteStructure |= result.IsSuccess;
            operation = await ApplyWriteResultAsync(
                operation,
                runningStep,
                result,
                GetStructureSuccessMessage(runningStep),
                cancellationToken).ConfigureAwait(false);
            if (MustStopWritePass(result.FailureKind))
            {
                break;
            }
        }

        if (!wroteStructure)
        {
            return operation;
        }

        var refresh = await liveFleetService
            .RefreshCurrentAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!TryValidateSnapshot(operation, refresh, out var refreshedSnapshot, out var failure))
        {
            return await SaveOperationAsync(
                operation with
                {
                    State = OperationState.NeedsAttention,
                    Message = failure,
                },
                cancellationToken).ConfigureAwait(false);
        }

        snapshot = refreshedSnapshot!;
        operation = ReconcileWithSnapshot(
            operation,
            snapshot,
            initialSnapshot,
            timeProvider.GetUtcNow());
        return await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FleetOperation> RunCommanderPhaseAsync(
        FleetOperation operation,
        LiveFleetSnapshot initialSnapshot,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var now = timeProvider.GetUtcNow();
            var candidate = operation.Steps
                .Where(step =>
                    step.Type == FleetOperationStepType.PromoteCommander &&
                    step.State == FleetOperationStepState.Pending &&
                    (step.RetryAfterUtc is null || step.RetryAfterUtc <= now))
                .OrderBy(step => step.SortOrder)
                .FirstOrDefault();
            if (candidate is null)
            {
                return operation;
            }

            var refresh = await liveFleetService
                .RefreshCurrentAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!TryValidateSnapshot(operation, refresh, out var snapshot, out var failure))
            {
                return await SaveOperationAsync(
                    operation with
                    {
                        State = OperationState.NeedsAttention,
                        Message = failure,
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            operation = ReconcileWithSnapshot(
                operation,
                snapshot!,
                initialSnapshot,
                timeProvider.GetUtcNow());
            candidate = operation.Steps.FirstOrDefault(step =>
                string.Equals(step.StepKey, candidate.StepKey, StringComparison.Ordinal) &&
                step.State == FleetOperationStepState.Pending);
            if (candidate is null)
            {
                operation = await SaveOperationAsync(operation, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var occupant = FindCommanderSlotOccupant(snapshot!, candidate.Target);
            if (occupant is not null)
            {
                var failed = candidate with
                {
                    State = FleetOperationStepState.Failed,
                    LastFailureKind = FleetWriteFailureKind.Client.ToString(),
                    Message = $"Commander slot is occupied by {occupant.CharacterName}. No demotion was sent; refresh the profile plan.",
                    UpdatedAtUtc = timeProvider.GetUtcNow(),
                };
                operation = ReplaceStep(operation, failed, failed.UpdatedAtUtc);
                return await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);
            }

            operation = await MarkRunningAsync(operation, candidate, cancellationToken)
                .ConfigureAwait(false);
            var runningStep = FindStep(operation, candidate.StepKey);
            var result = await writeService
                .PromoteCommanderAsync(operation.FleetId, runningStep.Target, cancellationToken)
                .ConfigureAwait(false);
            operation = await ApplyWriteResultAsync(
                operation,
                runningStep,
                result,
                $"Commander change sent for {runningStep.Target.CharacterName}; verifying the live role.",
                cancellationToken).ConfigureAwait(false);
            if (MustStopWritePass(result.FailureKind))
            {
                return operation;
            }

            refresh = await liveFleetService
                .RefreshCurrentAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!TryValidateSnapshot(operation, refresh, out snapshot, out failure))
            {
                return await SaveOperationAsync(
                    operation with
                    {
                        State = OperationState.NeedsAttention,
                        Message = failure,
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            operation = ReconcileWithSnapshot(
                operation,
                snapshot!,
                initialSnapshot,
                timeProvider.GetUtcNow());
            operation = await SaveOperationAsync(operation, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<FleetWriteResult> ExecuteStructureWriteAsync(
        long fleetId,
        FleetOperationStep step,
        CancellationToken cancellationToken) =>
        step.Type switch
        {
            FleetOperationStepType.CreateWing => await writeService
                .CreateWingAsync(fleetId, cancellationToken)
                .ConfigureAwait(false),
            FleetOperationStepType.RenameWing => await writeService
                .RenameWingAsync(fleetId, step.Target, cancellationToken)
                .ConfigureAwait(false),
            FleetOperationStepType.CreateSquad => await writeService
                .CreateSquadAsync(fleetId, step.Target, cancellationToken)
                .ConfigureAwait(false),
            FleetOperationStepType.RenameSquad => await writeService
                .RenameSquadAsync(fleetId, step.Target, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"Step '{step.StepKey}' is not a structure write."),
        };

    private async Task<FleetOperation> MarkRunningAsync(
        FleetOperation operation,
        FleetOperationStep step,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var updatedStep = step with
        {
            State = FleetOperationStepState.Running,
            Attempts = step.Attempts + 1,
            LastFailureKind = null,
            Message = "Write in progress…",
            RetryAfterUtc = null,
            UpdatedAtUtc = now,
        };
        operation = ReplaceStep(operation, updatedStep, now);
        return await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FleetOperation> ApplyWriteResultAsync(
        FleetOperation operation,
        FleetOperationStep step,
        FleetWriteResult result,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        FleetOperationStep updatedStep;
        if (result.IsSuccess)
        {
            updatedStep = step with
            {
                State = FleetOperationStepState.Waiting,
                LastFailureKind = null,
                Message = successMessage,
                RetryAfterUtc = null,
                UpdatedAtUtc = now,
            };
        }
        else if (result.FailureKind == FleetWriteFailureKind.RateLimited)
        {
            updatedStep = step with
            {
                State = FleetOperationStepState.Pending,
                LastFailureKind = result.FailureKind.ToString(),
                Message = result.UserMessage,
                RetryAfterUtc = now.Add(result.RetryAfter ?? TimeSpan.FromSeconds(60)),
                UpdatedAtUtc = now,
            };
        }
        else
        {
            var requestSuffix = string.IsNullOrWhiteSpace(result.RequestId)
                ? string.Empty
                : $" Request ID: {result.RequestId}.";
            updatedStep = step with
            {
                State = FleetOperationStepState.Failed,
                LastFailureKind = result.FailureKind.ToString(),
                Message = result.UserMessage + requestSuffix,
                RetryAfterUtc = null,
                UpdatedAtUtc = now,
            };
        }

        operation = ReplaceStep(operation, updatedStep, now);
        return await SaveOperationAsync(operation, cancellationToken).ConfigureAwait(false);
    }

    private static FleetOperation ReconcileWithSnapshot(
        FleetOperation operation,
        LiveFleetSnapshot snapshot,
        LiveFleetSnapshot initialSnapshot,
        DateTimeOffset now)
    {
        operation = RecoverInterruptedStructureCreate(
            operation,
            snapshot,
            initialSnapshot,
            now);
        foreach (var structureStep in operation.Steps
            .Where(IsStructureStep)
            .OrderBy(step => step.SortOrder)
            .ToArray())
        {
            operation = ReconcileStructureStep(operation, structureStep, snapshot, now);
        }

        var members = snapshot.Members.ToDictionary(member => member.CharacterId);
        var steps = operation.Steps.Select(step =>
        {
            if (step.State == FleetOperationStepState.Skipped ||
                IsStructureStep(step) ||
                step.Type == FleetOperationStepType.DeferredCommander)
            {
                return step;
            }

            var isPresent = members.TryGetValue(step.Target.CharacterId, out var member);
            if (step.Type == FleetOperationStepType.Invite)
            {
                if (isPresent)
                {
                    return step with
                    {
                        State = FleetOperationStepState.Succeeded,
                        LastFailureKind = null,
                        Message = "Invitation accepted; the character is now in fleet.",
                        RetryAfterUtc = null,
                        UpdatedAtUtc = now,
                    };
                }

                return step.State == FleetOperationStepState.Running
                    ? UnknownOutcomeStep(
                        step,
                        "The app stopped while this invite was being sent. Its outcome is unknown; retry explicitly only if the character is still absent.",
                        now)
                    : step;
            }

            if (step.Type == FleetOperationStepType.Place)
            {
                if (!isPresent)
                {
                    return step.State == FleetOperationStepState.Failed
                        ? step
                        : step with
                        {
                            State = FleetOperationStepState.Waiting,
                            Message = "Waiting for the character to join the fleet.",
                            UpdatedAtUtc = now,
                        };
                }

                if (member is not null &&
                    (BasePlacementMatches(member, step.Target) ||
                        FinalCommanderPlacementMatches(member, step.Target)))
                {
                    return step with
                    {
                        State = FleetOperationStepState.Succeeded,
                        LastFailureKind = null,
                        Message = $"Safe base placement confirmed for {step.Target.CharacterName}.",
                        RetryAfterUtc = null,
                        UpdatedAtUtc = now,
                    };
                }

                if (step.State == FleetOperationStepState.Running)
                {
                    return UnknownOutcomeStep(
                        step,
                        "The app stopped during this placement. The live position still differs; retry explicitly after checking EVE.",
                        now);
                }

                if (step.State == FleetOperationStepState.Waiting && step.Attempts > 0)
                {
                    return AwaitVerificationOrFail(
                        step,
                        "ESI accepted the placement, but the target position is not confirmed yet.",
                        now);
                }

                return step.State == FleetOperationStepState.Failed
                    ? step
                    : step with
                    {
                        State = FleetOperationStepState.Pending,
                        Message = "Live position differs from the reviewed safe base placement.",
                        UpdatedAtUtc = now,
                    };
            }

            if (step.Type != FleetOperationStepType.PromoteCommander)
            {
                return step;
            }

            if (!isPresent)
            {
                return step.State == FleetOperationStepState.Failed
                    ? step
                    : step with
                    {
                        State = FleetOperationStepState.Waiting,
                        Message = "Waiting for the character to join the fleet.",
                        UpdatedAtUtc = now,
                    };
            }

            if (member is not null && FinalCommanderPlacementMatches(member, step.Target))
            {
                return step with
                {
                    State = FleetOperationStepState.Succeeded,
                    LastFailureKind = null,
                    Message = $"Confirmed {GetDesiredRoleLabel(step.Target.DesiredRole)} for {step.Target.CharacterName}.",
                    RetryAfterUtc = null,
                    UpdatedAtUtc = now,
                };
            }

            if (step.State == FleetOperationStepState.Running)
            {
                return UnknownOutcomeStep(
                    step,
                    "The app stopped during this commander change. The desired role is not visible; retry explicitly after checking EVE.",
                    now);
            }

            if (step.State == FleetOperationStepState.Waiting && step.Attempts > 0)
            {
                return AwaitVerificationOrFail(
                    step,
                    "ESI accepted the commander change, but the desired role is not confirmed yet.",
                    now);
            }

            var baseStep = operation.Steps.FirstOrDefault(candidate =>
                candidate.Type == FleetOperationStepType.Place &&
                candidate.Target.CharacterId == step.Target.CharacterId);
            return baseStep is not null &&
                baseStep.State is not FleetOperationStepState.Succeeded and
                    not FleetOperationStepState.Skipped
                ? step with
                {
                    State = FleetOperationStepState.Waiting,
                    Message = "Waiting for safe squad-member staging.",
                    UpdatedAtUtc = now,
                }
                : step.State == FleetOperationStepState.Failed
                    ? step
                    : step with
                    {
                        State = FleetOperationStepState.Pending,
                        Message = "Ready for serialized commander promotion.",
                        UpdatedAtUtc = now,
                    };
        }).ToArray();

        return operation with
        {
            Steps = steps,
            UpdatedAtUtc = now,
        };
    }

    private static FleetOperation FinalizeState(
        FleetOperation operation,
        DateTimeOffset now)
    {
        OperationState state;
        string message;
        if (operation.Steps.Any(step => step.State == FleetOperationStepState.Failed))
        {
            state = OperationState.NeedsAttention;
            message = "One or more steps need attention. Retry or skip only after checking the live fleet.";
        }
        else if (operation.Steps.Any(step =>
            IsStructureStep(step) &&
            step.State is not FleetOperationStepState.Succeeded and
                not FleetOperationStepState.Skipped))
        {
            state = OperationState.EnsureStructure;
            message = "Fleet structure repair is not yet confirmed. Continue after checking the shown step.";
        }
        else if (operation.Steps.Any(step =>
            step.State == FleetOperationStepState.Pending &&
            step.RetryAfterUtc is not null))
        {
            var pausedStep = operation.Steps.First(step =>
                step.State == FleetOperationStepState.Pending &&
                step.RetryAfterUtc is not null);
            state = GetOperationState(pausedStep);
            message = "ESI paused fleet writes. Continue after the retry time shown on the pending step.";
        }
        else if (operation.Steps.Any(step =>
            step.Type == FleetOperationStepType.Invite &&
            step.State == FleetOperationStepState.Pending))
        {
            state = OperationState.InviteMissing;
            message = "An invitation is queued behind an ESI pause. Continue after the shown retry time.";
        }
        else if (operation.Steps.Any(step =>
            step.Type == FleetOperationStepType.Invite &&
            step.State == FleetOperationStepState.Waiting))
        {
            state = OperationState.AwaitAcceptance;
            message = "Accept the pending invitations in EVE. Fleet Desk checks automatically every 30 seconds while open; Check now is also available.";
        }
        else if (operation.Steps.Any(step =>
            step.Type == FleetOperationStepType.Place &&
            step.State is FleetOperationStepState.Pending or FleetOperationStepState.Waiting))
        {
            state = OperationState.PlaceMembers;
            message = "Safe squad-member staging remains. Continue after accepted characters appear in fleet.";
        }
        else if (operation.Steps.Any(step =>
            step.Type == FleetOperationStepType.PromoteCommander &&
            step.State is FleetOperationStepState.Pending or FleetOperationStepState.Waiting))
        {
            state = OperationState.AssignCommanders;
            message = "Serialized commander changes remain. Continue to refresh and verify the next transition.";
        }
        else
        {
            state = OperationState.Complete;
            message = operation.Steps.Any(step => step.State == FleetOperationStepState.Skipped)
                ? "Milestone 5 verification finished. Skipped work remains visible and was not retried."
                : "Milestone 5 verification confirms the requested structure, placements, and commander roles.";
        }

        return operation with
        {
            State = state,
            Message = message,
            UpdatedAtUtc = now,
        };
    }

    private static bool BasePlacementMatches(
        LiveFleetMember member,
        FleetOperationTarget target) =>
        member.WingId == target.WingId &&
        member.SquadId == target.SquadId &&
        string.Equals(member.Role, "squad_member", StringComparison.Ordinal);

    private static FleetOperation RecoverInterruptedStructureCreate(
        FleetOperation operation,
        LiveFleetSnapshot snapshot,
        LiveFleetSnapshot initialSnapshot,
        DateTimeOffset now)
    {
        foreach (var interrupted in operation.Steps.Where(step =>
            (step.Type is FleetOperationStepType.CreateWing or
                FleetOperationStepType.CreateSquad) &&
            (step.Type == FleetOperationStepType.CreateWing
                ? step.Target.WingId == 0
                : step.Target.SquadId == 0) &&
            (step.State == FleetOperationStepState.Running ||
                (step.State == FleetOperationStepState.Failed &&
                    step.LastFailureKind is nameof(FleetWriteFailureKind.Network) or
                        nameof(FleetWriteFailureKind.Server)))).ToArray())
        {
            var assignedWingIds = operation.Steps
                .Select(step => step.Target.WingId)
                .Where(id => id > 0)
                .ToHashSet();
            var assignedSquadIds = operation.Steps
                .Select(step => step.Target.SquadId)
                .Where(id => id > 0)
                .ToHashSet();
            long[] candidates;
            if (interrupted.Type == FleetOperationStepType.CreateWing)
            {
                var exact = snapshot.Wings
                    .Where(wing => NamesMatch(wing.Name, interrupted.Target.WingName))
                    .Select(wing => wing.WingId)
                    .ToArray();
                var originalIds = initialSnapshot.Wings
                    .Select(wing => wing.WingId)
                    .ToHashSet();
                candidates = exact.Length == 1
                    ? exact
                    : snapshot.Wings
                        .Where(wing =>
                            !originalIds.Contains(wing.WingId) &&
                            !assignedWingIds.Contains(wing.WingId))
                        .Select(wing => wing.WingId)
                        .ToArray();
            }
            else
            {
                var wing = snapshot.Wings.FirstOrDefault(candidate =>
                    candidate.WingId == interrupted.Target.WingId);
                var exact = wing?.Squads
                    .Where(squad => NamesMatch(squad.Name, interrupted.Target.SquadName))
                    .Select(squad => squad.SquadId)
                    .ToArray() ?? [];
                var originalIds = initialSnapshot.Wings
                    .SelectMany(candidate => candidate.Squads)
                    .Select(squad => squad.SquadId)
                    .ToHashSet();
                candidates = exact.Length == 1
                    ? exact
                    : wing?.Squads
                        .Where(squad =>
                            !originalIds.Contains(squad.SquadId) &&
                            !assignedSquadIds.Contains(squad.SquadId))
                        .Select(squad => squad.SquadId)
                        .ToArray() ?? [];
            }

            if (candidates.Length == 1)
            {
                operation = PropagateCreatedId(
                    operation,
                    interrupted,
                    candidates[0],
                    now);
                continue;
            }

            var failed = interrupted with
            {
                State = FleetOperationStepState.Failed,
                LastFailureKind = FleetWriteFailureKind.Network.ToString(),
                Message = candidates.Length == 0
                    ? "The app stopped during this create request and no unique new structure can be confirmed. Retry explicitly after checking EVE."
                    : "More than one new structure appeared while the app was closed. Fleet Organizer cannot choose one safely.",
                UpdatedAtUtc = now,
            };
            operation = ReplaceStep(operation, failed, now);
        }

        return operation;
    }

    private static FleetOperation ReconcileStructureStep(
        FleetOperation operation,
        FleetOperationStep originalStep,
        LiveFleetSnapshot snapshot,
        DateTimeOffset now)
    {
        var step = FindStep(operation, originalStep.StepKey);
        if (step.State == FleetOperationStepState.Skipped)
        {
            return operation;
        }

        if (step.Type == FleetOperationStepType.CreateWing)
        {
            var wing = step.Target.WingId > 0
                ? snapshot.Wings.FirstOrDefault(candidate =>
                    candidate.WingId == step.Target.WingId)
                : FindUniqueWingByName(snapshot, step.Target.WingName);
            if (wing is not null)
            {
                operation = PropagateCreatedId(operation, step, wing.WingId, now);
                step = FindStep(operation, step.StepKey);
                return ReplaceStep(
                    operation,
                    ConfirmedStep(step, $"Created wing ID {wing.WingId} confirmed.", now),
                    now);
            }

            return step.State == FleetOperationStepState.Running
                ? ReplaceStep(
                    operation,
                    UnknownOutcomeStep(
                        step,
                        "The app stopped during wing creation and the new wing is not uniquely visible.",
                        now),
                    now)
                : step.State == FleetOperationStepState.Waiting && step.Attempts > 0
                    ? ReplaceStep(
                        operation,
                        AwaitVerificationOrFail(
                            step,
                            "ESI returned a wing ID, but the wing is not visible yet.",
                            now),
                        now)
                    : operation;
        }

        if (step.Type == FleetOperationStepType.RenameWing)
        {
            if (step.Target.WingId == 0)
            {
                return operation;
            }

            var wing = snapshot.Wings.FirstOrDefault(candidate =>
                candidate.WingId == step.Target.WingId);
            if (wing is not null && NamesMatch(wing.Name, step.Target.WingName))
            {
                return ReplaceStep(
                    operation,
                    ConfirmedStep(step, $"Wing name '{step.Target.WingName}' confirmed.", now),
                    now);
            }

            if (step.State == FleetOperationStepState.Running)
            {
                return ReplaceStep(
                    operation,
                    UnknownOutcomeStep(
                        step,
                        "The app stopped during this wing rename and the desired name is not visible.",
                        now),
                    now);
            }

            if (step.State == FleetOperationStepState.Waiting && step.Attempts > 0)
            {
                return ReplaceStep(
                    operation,
                    AwaitVerificationOrFail(
                        step,
                        "ESI accepted the wing rename, but the desired name is not confirmed yet.",
                        now),
                    now);
            }

            return step.State == FleetOperationStepState.Waiting && step.Attempts == 0
                ? ReplaceStep(
                    operation,
                    step with
                    {
                        State = FleetOperationStepState.Pending,
                        Message = "Parent wing is ready; rename can run.",
                        UpdatedAtUtc = now,
                    },
                    now)
                : operation;
        }

        var liveWing = snapshot.Wings.FirstOrDefault(candidate =>
            candidate.WingId == step.Target.WingId);
        if (step.Type == FleetOperationStepType.CreateSquad)
        {
            var squad = step.Target.SquadId > 0
                ? liveWing?.Squads.FirstOrDefault(candidate =>
                    candidate.SquadId == step.Target.SquadId)
                : FindUniqueSquadByName(liveWing, step.Target.SquadName);
            if (squad is not null)
            {
                operation = PropagateCreatedId(operation, step, squad.SquadId, now);
                step = FindStep(operation, step.StepKey);
                return ReplaceStep(
                    operation,
                    ConfirmedStep(step, $"Created squad ID {squad.SquadId} confirmed.", now),
                    now);
            }

            return step.State == FleetOperationStepState.Running
                ? ReplaceStep(
                    operation,
                    UnknownOutcomeStep(
                        step,
                        "The app stopped during squad creation and the new squad is not uniquely visible.",
                        now),
                    now)
                : step.State == FleetOperationStepState.Waiting && step.Attempts > 0
                    ? ReplaceStep(
                        operation,
                        AwaitVerificationOrFail(
                            step,
                            "ESI returned a squad ID, but the squad is not visible yet.",
                            now),
                        now)
                    : operation;
        }

        if (step.Type != FleetOperationStepType.RenameSquad || step.Target.SquadId == 0)
        {
            return operation;
        }

        var liveSquad = liveWing?.Squads.FirstOrDefault(candidate =>
            candidate.SquadId == step.Target.SquadId);
        if (liveSquad is not null && NamesMatch(liveSquad.Name, step.Target.SquadName))
        {
            return ReplaceStep(
                operation,
                ConfirmedStep(step, $"Squad name '{step.Target.SquadName}' confirmed.", now),
                now);
        }

        if (step.State == FleetOperationStepState.Running)
        {
            return ReplaceStep(
                operation,
                UnknownOutcomeStep(
                    step,
                    "The app stopped during this squad rename and the desired name is not visible.",
                    now),
                now);
        }

        if (step.State == FleetOperationStepState.Waiting && step.Attempts > 0)
        {
            return ReplaceStep(
                operation,
                AwaitVerificationOrFail(
                    step,
                    "ESI accepted the squad rename, but the desired name is not confirmed yet.",
                    now),
                now);
        }

        return step.State == FleetOperationStepState.Waiting && step.Attempts == 0
            ? ReplaceStep(
                operation,
                step with
                {
                    State = FleetOperationStepState.Pending,
                    Message = "Parent squad is ready; rename can run.",
                    UpdatedAtUtc = now,
                },
                now)
            : operation;
    }

    private static FleetOperation ActivateReadyStructureSteps(
        FleetOperation operation,
        DateTimeOffset now) =>
        operation with
        {
            Steps = operation.Steps.Select(step =>
                IsStructureStep(step) &&
                step.State == FleetOperationStepState.Waiting &&
                step.Attempts == 0 &&
                StructureTargetHasRequiredIds(step)
                    ? step with
                    {
                        State = FleetOperationStepState.Pending,
                        Message = "Structure dependency is ready.",
                        UpdatedAtUtc = now,
                    }
                    : step).ToArray(),
            UpdatedAtUtc = now,
        };

    private static FleetOperation PropagateCreatedId(
        FleetOperation operation,
        FleetOperationStep createdStep,
        long createdId,
        DateTimeOffset now)
    {
        var steps = operation.Steps.Select(step =>
        {
            if (createdStep.Type == FleetOperationStepType.CreateWing &&
                step.Target.WingId == 0 &&
                NamesMatch(step.Target.WingName, createdStep.Target.WingName))
            {
                return step with
                {
                    Target = step.Target with { WingId = createdId },
                    UpdatedAtUtc = now,
                };
            }

            if (createdStep.Type == FleetOperationStepType.CreateSquad &&
                step.Target.SquadId == 0 &&
                NamesMatch(step.Target.WingName, createdStep.Target.WingName) &&
                NamesMatch(step.Target.SquadName, createdStep.Target.SquadName))
            {
                return step with
                {
                    Target = step.Target with { SquadId = createdId },
                    UpdatedAtUtc = now,
                };
            }

            return step;
        }).ToArray();
        return operation with { Steps = steps, UpdatedAtUtc = now };
    }

    private static FleetOperationStep ConfirmedStep(
        FleetOperationStep step,
        string message,
        DateTimeOffset now) =>
        step with
        {
            State = FleetOperationStepState.Succeeded,
            LastFailureKind = null,
            Message = message,
            RetryAfterUtc = null,
            UpdatedAtUtc = now,
        };

    private static FleetOperationStep UnknownOutcomeStep(
        FleetOperationStep step,
        string message,
        DateTimeOffset now) =>
        step with
        {
            State = FleetOperationStepState.Failed,
            LastFailureKind = FleetWriteFailureKind.Network.ToString(),
            Message = message,
            RetryAfterUtc = null,
            UpdatedAtUtc = now,
        };

    private static FleetOperationStep AwaitVerificationOrFail(
        FleetOperationStep step,
        string waitingMessage,
        DateTimeOffset now) =>
        now - step.UpdatedAtUtc < TimeSpan.FromSeconds(5)
            ? step with
            {
                Message = waitingMessage,
                UpdatedAtUtc = now,
            }
            : step with
            {
                State = FleetOperationStepState.Failed,
                LastFailureKind = FleetWriteFailureKind.InvalidResponse.ToString(),
                Message = waitingMessage + " Refresh EVE, then retry explicitly if it still differs.",
                RetryAfterUtc = null,
                UpdatedAtUtc = now,
            };

    private static bool FinalCommanderPlacementMatches(
        LiveFleetMember member,
        FleetOperationTarget target) =>
        target.DesiredRole switch
        {
            DesiredFleetRole.SquadCommander =>
                member.WingId == target.WingId &&
                member.SquadId == target.SquadId &&
                string.Equals(member.Role, "squad_commander", StringComparison.Ordinal),
            DesiredFleetRole.WingCommander =>
                member.WingId == target.WingId &&
                string.Equals(member.Role, "wing_commander", StringComparison.Ordinal),
            _ => false,
        };

    private static LiveFleetMember? FindCommanderSlotOccupant(
        LiveFleetSnapshot snapshot,
        FleetOperationTarget target) =>
        snapshot.Members.FirstOrDefault(member =>
            member.CharacterId != target.CharacterId &&
            (target.DesiredRole == DesiredFleetRole.WingCommander
                ? member.WingId == target.WingId &&
                    string.Equals(member.Role, "wing_commander", StringComparison.Ordinal)
                : member.WingId == target.WingId &&
                    member.SquadId == target.SquadId &&
                    string.Equals(member.Role, "squad_commander", StringComparison.Ordinal)));

    private static string GetDesiredRoleLabel(DesiredFleetRole role) => role switch
    {
        DesiredFleetRole.SquadCommander => "Squad Commander",
        DesiredFleetRole.WingCommander => "Wing Commander",
        _ => role.ToString(),
    };

    private static string GetStructureSuccessMessage(FleetOperationStep step) => step.Type switch
    {
        FleetOperationStepType.CreateWing => $"Wing created for '{step.Target.WingName}'; verifying its ID.",
        FleetOperationStepType.RenameWing => $"Wing rename sent for '{step.Target.WingName}'; verifying the name.",
        FleetOperationStepType.CreateSquad => $"Squad created for '{step.Target.SquadName}'; verifying its ID.",
        FleetOperationStepType.RenameSquad => $"Squad rename sent for '{step.Target.SquadName}'; verifying the name.",
        _ => "Structure write accepted; verifying the live hierarchy.",
    };

    private static OperationState GetOperationState(FleetOperationStep step) => step.Type switch
    {
        FleetOperationStepType.CreateWing or
        FleetOperationStepType.RenameWing or
        FleetOperationStepType.CreateSquad or
        FleetOperationStepType.RenameSquad => OperationState.EnsureStructure,
        FleetOperationStepType.Invite => OperationState.InviteMissing,
        FleetOperationStepType.Place => OperationState.PlaceMembers,
        FleetOperationStepType.PromoteCommander => OperationState.AssignCommanders,
        _ => OperationState.NeedsAttention,
    };

    private static FleetOperationStep FindStep(
        FleetOperation operation,
        string stepKey) =>
        operation.Steps.Single(step =>
            string.Equals(step.StepKey, stepKey, StringComparison.Ordinal));

    private static bool IsStructureStep(FleetOperationStep step) =>
        step.Type is
            FleetOperationStepType.CreateWing or
            FleetOperationStepType.RenameWing or
            FleetOperationStepType.CreateSquad or
            FleetOperationStepType.RenameSquad;

    private static bool StructureTargetHasRequiredIds(FleetOperationStep step) => step.Type switch
    {
        FleetOperationStepType.CreateWing => true,
        FleetOperationStepType.RenameWing => step.Target.WingId > 0,
        FleetOperationStepType.CreateSquad => step.Target.WingId > 0,
        FleetOperationStepType.RenameSquad =>
            step.Target.WingId > 0 && step.Target.SquadId > 0,
        _ => false,
    };

    private static bool HasUnfinishedSteps(
        FleetOperation operation,
        Func<FleetOperationStep, bool> predicate) =>
        operation.Steps.Any(step =>
            predicate(step) &&
            step.State is not FleetOperationStepState.Succeeded and
                not FleetOperationStepState.Skipped);

    private static bool NamesMatch(string first, string second) =>
        string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);

    private static LiveFleetWing? FindUniqueWingByName(
        LiveFleetSnapshot snapshot,
        string name)
    {
        var matches = snapshot.Wings
            .Where(wing => NamesMatch(wing.Name, name))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static LiveFleetSquad? FindUniqueSquadByName(
        LiveFleetWing? wing,
        string name)
    {
        var matches = wing?.Squads
            .Where(squad => NamesMatch(squad.Name, name))
            .Take(2)
            .ToArray() ?? [];
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool MustStopWritePass(FleetWriteFailureKind failureKind) =>
        failureKind is
            FleetWriteFailureKind.RateLimited or
            FleetWriteFailureKind.Unauthorized or
            FleetWriteFailureKind.Forbidden or
            FleetWriteFailureKind.NotFound;

    private async Task<FleetOperation> RequireOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken) =>
        await repository.LoadAsync(operationId, cancellationToken).ConfigureAwait(false) ??
        throw new InvalidOperationException("The saved fleet operation could not be found.");

    private async Task<FleetOperation> SaveOperationAsync(
        FleetOperation operation,
        CancellationToken cancellationToken)
    {
        var updated = operation with { UpdatedAtUtc = timeProvider.GetUtcNow() };
        await repository.SaveAsync(updated, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return updated;
    }

    private static FleetOperation ReplaceStep(
        FleetOperation operation,
        FleetOperationStep updatedStep,
        DateTimeOffset now) =>
        operation with
        {
            Steps = operation.Steps
                .Select(step => string.Equals(
                    step.StepKey,
                    updatedStep.StepKey,
                    StringComparison.Ordinal)
                        ? updatedStep
                        : step)
                .ToArray(),
            UpdatedAtUtc = now,
        };

    private static LiveFleetSnapshot RequireReadySnapshot(LiveFleetLoadResult result)
    {
        if (result.Status != LiveFleetLoadStatus.Ready || result.Snapshot is null)
        {
            throw new InvalidOperationException(result.UserMessage);
        }

        return result.Snapshot;
    }

    private static bool TryValidateSnapshot(
        FleetOperation operation,
        LiveFleetLoadResult result,
        out LiveFleetSnapshot? snapshot,
        out string failureMessage)
    {
        snapshot = result.Snapshot;
        if (result.Status != LiveFleetLoadStatus.Ready || snapshot is null)
        {
            failureMessage = result.UserMessage;
            return false;
        }

        if (snapshot.FleetId != operation.FleetId)
        {
            failureMessage =
                $"The signed-in character is now in fleet {snapshot.FleetId}, but this operation belongs to fleet {operation.FleetId}. The saved run was not redirected.";
            return false;
        }

        if (!snapshot.IsFleetBoss)
        {
            failureMessage =
                "Fleet-boss authority was lost. No further write will be sent until the signed-in character is fleet boss again.";
            return false;
        }

        failureMessage = string.Empty;
        return true;
    }
}

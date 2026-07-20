using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Planning;

namespace FleetOrganizer.Core.Operations;

public enum FleetOperationStepType
{
    Invite,
    Place,
    DeferredCommander,
    CreateWing,
    RenameWing,
    CreateSquad,
    RenameSquad,
    PromoteCommander,
}

public enum FleetOperationStepState
{
    Pending,
    Running,
    Waiting,
    Succeeded,
    Failed,
    Skipped,
}

public enum FleetWriteFailureKind
{
    None,
    Unauthorized,
    Forbidden,
    NotFound,
    Validation,
    RateLimited,
    Server,
    Network,
    InvalidResponse,
    Client,
}

public sealed record FleetOperationTarget(
    long CharacterId,
    string CharacterName,
    string WingName,
    string SquadName,
    long WingId,
    long SquadId,
    DesiredFleetRole DesiredRole,
    string? PreviousName = null,
    bool InviteDirectlyToRole = false);

public sealed record FleetOperationStep(
    string StepKey,
    int SortOrder,
    FleetOperationStepType Type,
    FleetOperationTarget Target,
    FleetOperationStepState State,
    int Attempts,
    string? LastFailureKind,
    string? Message,
    DateTimeOffset? RetryAfterUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record FleetOperation(
    Guid Id,
    Guid ProfileId,
    string ProfileName,
    long FleetId,
    OperationState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? Message,
    FleetOperationStep[] Steps)
{
    public int PendingSteps => Steps.Count(step =>
        step.State is FleetOperationStepState.Pending or FleetOperationStepState.Running);

    public int WaitingSteps => Steps.Count(step => step.State == FleetOperationStepState.Waiting);

    public int SucceededSteps => Steps.Count(step => step.State == FleetOperationStepState.Succeeded);

    public int FailedSteps => Steps.Count(step => step.State == FleetOperationStepState.Failed);

    public int SkippedSteps => Steps.Count(step => step.State == FleetOperationStepState.Skipped);

    public bool IsTerminal => State is OperationState.Complete or OperationState.Cancelled;
}

public sealed record FleetWriteResult(
    bool IsSuccess,
    FleetWriteFailureKind FailureKind,
    string UserMessage,
    string? RequestId,
    TimeSpan? RetryAfter,
    long? CreatedId = null);

public static class FleetOperationFactory
{
    public static FleetOperation Create(
        Guid operationId,
        FleetProfile profile,
        LiveFleetSnapshot snapshot,
        FleetDryRunPlan plan,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.ProfileId != profile.Id || plan.FleetId != snapshot.FleetId)
        {
            throw new InvalidOperationException(
                "The reviewed plan does not belong to this profile and live fleet.");
        }

        if (!plan.CanExecute)
        {
            throw new InvalidOperationException(
                "Resolve every dry-run safety blocker before starting an operation.");
        }

        var mappings = BuildStructureMappings(profile, snapshot, plan);
        var targets = BuildCharacterTargets(profile, mappings);
        var inviteCharacterIds = plan.Items
            .Where(item => item.Kind == FleetPlanItemKind.InviteCharacter)
            .Select(item => item.CharacterId)
            .OfType<long>()
            .ToHashSet();
        var changedCharacterIds = plan.Items
            .Where(item => item.Kind is FleetPlanItemKind.MoveCharacter or FleetPlanItemKind.ChangeRole)
            .Select(item => item.CharacterId)
            .OfType<long>()
            .ToHashSet();
        var structureSteps = new List<FleetOperationStep>();
        var inviteSteps = new List<FleetOperationStep>();
        var placementSteps = new List<FleetOperationStep>();
        var commanderSteps = new List<FleetOperationStep>();
        var sortOrder = 0;

        BuildStructureSteps(
            profile,
            plan,
            mappings,
            structureSteps,
            ref sortOrder,
            now);

        foreach (var assignment in profile.Assignments
            .OrderBy(assignment => assignment.CharacterName, StringComparer.OrdinalIgnoreCase))
        {
            if (assignment.DesiredRole == DesiredFleetRole.FleetCommander)
            {
                continue;
            }

            var target = targets[assignment.CharacterId];
            var isMissing = inviteCharacterIds.Contains(assignment.CharacterId);
            var needsChange = changedCharacterIds.Contains(assignment.CharacterId);

            if (isMissing)
            {
                inviteSteps.Add(CreateStep(
                    $"invite:{snapshot.FleetId}:{assignment.CharacterId}",
                    sortOrder++,
                    FleetOperationStepType.Invite,
                    target,
                    FleetOperationStepState.Pending,
                    "Ready to send a fleet invitation.",
                    now));
            }


            if (plan.Mode == FleetRunMode.InviteMissing)
            {
                continue;
            }

            var liveMember = snapshot.Members.FirstOrDefault(member =>
                member.CharacterId == assignment.CharacterId);
            var needsBasePlacement = isMissing ||
                (needsChange && (liveMember is null ||
                    !BasePlacementMatches(liveMember, target)));
            if (assignment.DesiredRole == DesiredFleetRole.SquadMember &&
                (isMissing || needsChange))
            {
                placementSteps.Add(CreateStep(
                    $"place:{snapshot.FleetId}:{assignment.CharacterId}",
                    sortOrder++,
                    FleetOperationStepType.Place,
                    target,
                    isMissing
                        ? FleetOperationStepState.Waiting
                        : FleetOperationStepState.Pending,
                    isMissing
                        ? "Waiting for the character to accept the invitation."
                        : "Ready to place as an ordinary squad member.",
                    now));
            }
            else if ((assignment.DesiredRole is
                    DesiredFleetRole.SquadCommander or DesiredFleetRole.WingCommander) &&
                (isMissing || needsChange))
            {
                if (needsBasePlacement)
                {
                    placementSteps.Add(CreateStep(
                        $"stage:{snapshot.FleetId}:{assignment.CharacterId}",
                        sortOrder++,
                        FleetOperationStepType.Place,
                        target,
                        isMissing
                            ? FleetOperationStepState.Waiting
                            : FleetOperationStepState.Pending,
                        isMissing
                            ? "Waiting for the character to accept before staging as a squad member."
                            : "Ready to stage as a squad member before commander promotion.",
                        now));
                }

                commanderSteps.Add(CreateStep(
                    $"commander:{snapshot.FleetId}:{assignment.CharacterId}",
                    sortOrder++,
                    FleetOperationStepType.PromoteCommander,
                    target,
                    isMissing || needsBasePlacement
                        ? FleetOperationStepState.Waiting
                        : FleetOperationStepState.Pending,
                    isMissing
                        ? "Waiting for invitation acceptance and safe staging."
                        : needsBasePlacement
                            ? "Waiting for safe squad-member staging."
                            : "Ready for serialized commander promotion.",
                    now));
            }
        }

        var steps = structureSteps
            .Concat(inviteSteps)
            .Concat(placementSteps)
            .Concat(commanderSteps)
            .Select((step, index) => step with { SortOrder = index })
            .ToArray();
        if (steps.Length == 0)
        {
            throw new InvalidOperationException(
                "This profile already matches the live fleet. There is no repair work to run.");
        }

        return new FleetOperation(
            operationId,
            profile.Id,
            profile.Name,
            snapshot.FleetId,
            structureSteps.Count > 0
                ? OperationState.EnsureStructure
                : inviteSteps.Count > 0
                    ? OperationState.InviteMissing
                    : placementSteps.Count > 0
                        ? OperationState.PlaceMembers
                        : OperationState.AssignCommanders,
            now,
            now,
            "Operation saved locally. No write has been sent yet.",
            steps);
    }

    public static string GetReviewSignature(FleetDryRunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return string.Join(
            "\n",
            [$"MODE|{plan.Mode}", .. plan.Items
                .Where(item => item.Kind != FleetPlanItemKind.AlreadyCorrect)
                .Select(item =>
                    $"{item.Kind}|{item.CharacterId}|{item.TargetSquadId}|" +
                    $"{item.LiveWingId}|{item.LiveSquadId}|{item.TargetWingName}|" +
                    $"{item.TargetSquadName}|{item.PreviousName}|{item.Title}")]);
    }

    private static StructureMappings BuildStructureMappings(
        FleetProfile profile,
        LiveFleetSnapshot snapshot,
        FleetDryRunPlan plan)
    {
        var wingIds = new Dictionary<Guid, long>();
        var squadIds = new Dictionary<Guid, long>();

        foreach (var profileWing in profile.Wings)
        {
            var wingChange = plan.Items.SingleOrDefault(item =>
                (item.Kind is FleetPlanItemKind.CreateWing or FleetPlanItemKind.RenameWing) &&
                NamesMatch(item.TargetWingName ?? string.Empty, profileWing.Name));
            var wingId = wingChange?.Kind switch
            {
                FleetPlanItemKind.CreateWing => 0,
                FleetPlanItemKind.RenameWing => wingChange.LiveWingId ?? 0,
                _ when plan.Mode == FleetRunMode.InviteMissing => snapshot.Wings
                    .SingleOrDefault(wing => NamesMatch(wing.Name, profileWing.Name))
                    ?.WingId ?? 0,
                _ => snapshot.Wings.Single(wing => NamesMatch(wing.Name, profileWing.Name)).WingId,
            };
            wingIds[profileWing.Id] = wingId;

            foreach (var profileSquad in profileWing.Squads)
            {
                var squadChange = plan.Items.SingleOrDefault(item =>
                    item.TargetSquadId == profileSquad.Id &&
                    (item.Kind is FleetPlanItemKind.CreateSquad or FleetPlanItemKind.RenameSquad));
                var squadId = squadChange?.Kind switch
                {
                    FleetPlanItemKind.CreateSquad => 0,
                    FleetPlanItemKind.RenameSquad => squadChange.LiveSquadId ?? 0,
                    _ when plan.Mode == FleetRunMode.InviteMissing && wingId > 0 => snapshot.Wings
                        .Single(wing => wing.WingId == wingId)
                        .Squads.SingleOrDefault(squad => NamesMatch(squad.Name, profileSquad.Name))
                        ?.SquadId ?? 0,
                    _ when plan.Mode == FleetRunMode.InviteMissing => 0,
                    _ when wingId > 0 => snapshot.Wings
                        .Single(wing => wing.WingId == wingId)
                        .Squads.Single(squad => NamesMatch(squad.Name, profileSquad.Name))
                        .SquadId,
                    _ => 0,
                };
                squadIds[profileSquad.Id] = squadId;
            }
        }

        return new StructureMappings(wingIds, squadIds);
    }

    private static Dictionary<long, FleetOperationTarget> BuildCharacterTargets(
        FleetProfile profile,
        StructureMappings mappings)
    {
        var squadDefinitions = profile.Wings
            .SelectMany(wing => wing.Squads.Select(squad => (Wing: wing, Squad: squad)))
            .ToDictionary(definition => definition.Squad.Id);
        var targets = new Dictionary<long, FleetOperationTarget>();

        foreach (var assignment in profile.Assignments)
        {
            var definition = squadDefinitions[assignment.TargetSquadId];
            targets[assignment.CharacterId] = new FleetOperationTarget(
                assignment.CharacterId,
                assignment.CharacterName,
                definition.Wing.Name,
                definition.Squad.Name,
                mappings.WingIds[definition.Wing.Id],
                mappings.SquadIds[definition.Squad.Id],
                assignment.DesiredRole);
        }

        return targets;
    }

    private static void BuildStructureSteps(
        FleetProfile profile,
        FleetDryRunPlan plan,
        StructureMappings mappings,
        List<FleetOperationStep> steps,
        ref int sortOrder,
        DateTimeOffset now)
    {
        foreach (var wing in profile.Wings.OrderBy(wing => wing.SortOrder))
        {
            var wingChange = plan.Items.SingleOrDefault(item =>
                (item.Kind is FleetPlanItemKind.CreateWing or FleetPlanItemKind.RenameWing) &&
                NamesMatch(item.TargetWingName ?? string.Empty, wing.Name));
            if (wingChange?.Kind == FleetPlanItemKind.CreateWing)
            {
                var target = CreateStructureTarget(
                    wing.Name,
                    string.Empty,
                    wingId: 0,
                    squadId: 0,
                    previousName: null);
                steps.Add(CreateStep(
                    $"create-wing:{wing.Id:D}",
                    sortOrder++,
                    FleetOperationStepType.CreateWing,
                    target,
                    FleetOperationStepState.Pending,
                    $"Ready to create the wing for '{wing.Name}'.",
                    now));
                steps.Add(CreateStep(
                    $"rename-wing:{wing.Id:D}",
                    sortOrder++,
                    FleetOperationStepType.RenameWing,
                    target with { PreviousName = "EVE default name" },
                    FleetOperationStepState.Waiting,
                    "Waiting for the new wing ID.",
                    now));
            }
            else if (wingChange?.Kind == FleetPlanItemKind.RenameWing)
            {
                steps.Add(CreateStep(
                    $"rename-wing:{wing.Id:D}",
                    sortOrder++,
                    FleetOperationStepType.RenameWing,
                    CreateStructureTarget(
                        wing.Name,
                        string.Empty,
                        wingChange.LiveWingId ?? 0,
                        squadId: 0,
                        wingChange.PreviousName),
                    FleetOperationStepState.Pending,
                    $"Ready to rename '{wingChange.PreviousName}' to '{wing.Name}'.",
                    now));
            }

            foreach (var squad in wing.Squads.OrderBy(squad => squad.SortOrder))
            {
                var squadChange = plan.Items.SingleOrDefault(item =>
                    item.TargetSquadId == squad.Id &&
                    (item.Kind is FleetPlanItemKind.CreateSquad or FleetPlanItemKind.RenameSquad));
                if (squadChange?.Kind == FleetPlanItemKind.CreateSquad)
                {
                    var target = CreateStructureTarget(
                        wing.Name,
                        squad.Name,
                        mappings.WingIds[wing.Id],
                        squadId: 0,
                        previousName: null);
                    steps.Add(CreateStep(
                        $"create-squad:{squad.Id:D}",
                        sortOrder++,
                        FleetOperationStepType.CreateSquad,
                        target,
                        mappings.WingIds[wing.Id] == 0
                            ? FleetOperationStepState.Waiting
                            : FleetOperationStepState.Pending,
                        mappings.WingIds[wing.Id] == 0
                            ? "Waiting for the new parent wing."
                            : $"Ready to create the squad for '{squad.Name}'.",
                        now));
                    steps.Add(CreateStep(
                        $"rename-squad:{squad.Id:D}",
                        sortOrder++,
                        FleetOperationStepType.RenameSquad,
                        target with { PreviousName = "EVE default name" },
                        FleetOperationStepState.Waiting,
                        "Waiting for the new squad ID.",
                        now));
                }
                else if (squadChange?.Kind == FleetPlanItemKind.RenameSquad)
                {
                    steps.Add(CreateStep(
                        $"rename-squad:{squad.Id:D}",
                        sortOrder++,
                        FleetOperationStepType.RenameSquad,
                        CreateStructureTarget(
                            wing.Name,
                            squad.Name,
                            squadChange.LiveWingId ?? 0,
                            squadChange.LiveSquadId ?? 0,
                            squadChange.PreviousName),
                        FleetOperationStepState.Pending,
                        $"Ready to rename '{squadChange.PreviousName}' to '{squad.Name}'.",
                        now));
                }
            }
        }
    }

    private static FleetOperationTarget CreateStructureTarget(
        string wingName,
        string squadName,
        long wingId,
        long squadId,
        string? previousName) =>
        new(
            CharacterId: 0,
            CharacterName: string.Empty,
            wingName,
            squadName,
            wingId,
            squadId,
            DesiredFleetRole.SquadMember,
            previousName);

    private static bool BasePlacementMatches(
        LiveFleetMember member,
        FleetOperationTarget target) =>
        member.WingId == target.WingId &&
        member.SquadId == target.SquadId &&
        string.Equals(member.Role, "squad_member", StringComparison.Ordinal);

    private static FleetOperationStep CreateStep(
        string stepKey,
        int sortOrder,
        FleetOperationStepType type,
        FleetOperationTarget target,
        FleetOperationStepState state,
        string message,
        DateTimeOffset now) =>
        new(
            stepKey,
            sortOrder,
            type,
            target,
            state,
            Attempts: 0,
            LastFailureKind: null,
            message,
            RetryAfterUtc: null,
            now);

    private static bool NamesMatch(string first, string second) =>
        string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record StructureMappings(
        Dictionary<Guid, long> WingIds,
        Dictionary<Guid, long> SquadIds);
}

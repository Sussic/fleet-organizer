using FleetOrganizer.Core.Domain;

namespace FleetOrganizer.Core.Profiles;

public static class ProfileValidator
{
    public const int MaximumHierarchyNameLength = 10;
    public const int MaximumWings = 5;
    public const int MaximumSquadsPerWing = 5;
    public const int MaximumCharactersPerSquad = 10;

    public static IReadOnlyList<ProfileValidationError> Validate(FleetProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var errors = new List<ProfileValidationError>();

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add(new(
                "profile.name.required",
                "The profile needs a name.",
                "profile.name"));
        }

        ValidateWings(profile.Wings, errors);
        ValidateAssignments(profile, errors);
        ValidateShipRules(profile, errors);

        return errors;
    }

    private static void ValidateWings(
        IReadOnlyList<ProfileWing> wings,
        List<ProfileValidationError> errors)
    {
        if (wings.Count > MaximumWings)
        {
            errors.Add(new(
                "profile.wings.capacity",
                $"A fleet can have at most {MaximumWings} wings.",
                "profile.wings"));
        }

        var wingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var wingIndex = 0; wingIndex < wings.Count; wingIndex++)
        {
            var wing = wings[wingIndex];
            var wingPath = $"profile.wings[{wingIndex}]";

            if (wing.Squads.Count > MaximumSquadsPerWing)
            {
                errors.Add(new(
                    "wing.squads.capacity",
                    $"Wing '{wing.Name}' can have at most {MaximumSquadsPerWing} squads.",
                    $"{wingPath}.squads"));
            }

            ValidateHierarchyName(wing.Name, "wing", wingPath, errors);

            if (!wingNames.Add(wing.Name.Trim()))
            {
                errors.Add(new(
                    "wing.name.duplicate",
                    $"Wing name '{wing.Name}' is used more than once.",
                    $"{wingPath}.name"));
            }

            var squadNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var squadIndex = 0; squadIndex < wing.Squads.Count; squadIndex++)
            {
                var squad = wing.Squads[squadIndex];
                var squadPath = $"{wingPath}.squads[{squadIndex}]";

                ValidateHierarchyName(squad.Name, "squad", squadPath, errors);

                if (!squadNames.Add(squad.Name.Trim()))
                {
                    errors.Add(new(
                        "squad.name.duplicate",
                        $"Squad name '{squad.Name}' is used more than once in wing '{wing.Name}'.",
                        $"{squadPath}.name"));
                }
            }
        }
    }

    private static void ValidateHierarchyName(
        string name,
        string nodeType,
        string path,
        List<ProfileValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(new(
                $"{nodeType}.name.required",
                $"The {nodeType} needs a name.",
                $"{path}.name"));
            return;
        }

        if (name.Trim().Length > MaximumHierarchyNameLength)
        {
            errors.Add(new(
                $"{nodeType}.name.too_long",
                $"The {nodeType} name must be {MaximumHierarchyNameLength} characters or fewer.",
                $"{path}.name"));
        }
    }

    private static void ValidateAssignments(
        FleetProfile profile,
        List<ProfileValidationError> errors)
    {
        var squadIds = profile.Wings
            .SelectMany(wing => wing.Squads)
            .Select(squad => squad.Id)
            .ToHashSet();

        var characterIds = new HashSet<long>();
        var squadCommanders = new HashSet<Guid>();
        var wingCommanders = new HashSet<Guid>();
        var fleetCommanderCount = 0;
        var squadAssignmentCounts = new Dictionary<Guid, int>();

        for (var assignmentIndex = 0; assignmentIndex < profile.Assignments.Count; assignmentIndex++)
        {
            var assignment = profile.Assignments[assignmentIndex];
            var assignmentPath = $"profile.assignments[{assignmentIndex}]";

            if (assignment.CharacterId <= 0)
            {
                errors.Add(new(
                    "assignment.character.invalid",
                    "The assignment has an invalid character ID.",
                    $"{assignmentPath}.characterId"));
            }

            if (!characterIds.Add(assignment.CharacterId))
            {
                errors.Add(new(
                    "assignment.character.duplicate",
                    $"Character '{assignment.CharacterName}' is assigned more than once.",
                    assignmentPath));
            }

            if (!squadIds.Contains(assignment.TargetSquadId))
            {
                errors.Add(new(
                    "assignment.squad.missing",
                    $"Character '{assignment.CharacterName}' targets a squad that is not in this profile.",
                    $"{assignmentPath}.targetSquadId"));
                continue;
            }

            if (assignment.DesiredRole is
                DesiredFleetRole.SquadMember or DesiredFleetRole.SquadCommander)
            {
                squadAssignmentCounts.TryGetValue(assignment.TargetSquadId, out var squadCount);
                squadAssignmentCounts[assignment.TargetSquadId] = squadCount + 1;
            }

            switch (assignment.DesiredRole)
            {
                case DesiredFleetRole.SquadCommander when !squadCommanders.Add(assignment.TargetSquadId):
                    errors.Add(new(
                        "assignment.squad_commander.duplicate",
                        "A squad can have only one desired squad commander.",
                        $"{assignmentPath}.desiredRole"));
                    break;

                case DesiredFleetRole.WingCommander:
                {
                    var wingId = profile.Wings
                        .Single(wing => wing.Squads.Any(squad => squad.Id == assignment.TargetSquadId))
                        .Id;

                    if (!wingCommanders.Add(wingId))
                    {
                        errors.Add(new(
                            "assignment.wing_commander.duplicate",
                            "A wing can have only one desired wing commander.",
                            $"{assignmentPath}.desiredRole"));
                    }

                    break;
                }

                case DesiredFleetRole.FleetCommander:
                    fleetCommanderCount++;
                    if (fleetCommanderCount > 1)
                    {
                        errors.Add(new(
                            "assignment.fleet_commander.duplicate",
                            "A profile can have only one desired fleet commander.",
                            $"{assignmentPath}.desiredRole"));
                    }

                    break;

                case DesiredFleetRole.SquadMember:
                case DesiredFleetRole.SquadCommander:
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Assignment for character {assignment.CharacterId} has unknown desired fleet role '{assignment.DesiredRole}'.");
            }
        }


        foreach (var (squadId, count) in squadAssignmentCounts)
        {
            if (count <= MaximumCharactersPerSquad)
            {
                continue;
            }

            var squad = profile.Wings
                .SelectMany(wing => wing.Squads.Select(candidate => (Wing: wing, Squad: candidate)))
                .Single(definition => definition.Squad.Id == squadId);
            errors.Add(new(
                "assignment.squad.capacity",
                $"{squad.Wing.Name} / {squad.Squad.Name} has {count} characters; a squad can hold at most {MaximumCharactersPerSquad}.",
                "profile.assignments"));
        }
    }

    private static void ValidateShipRules(
        FleetProfile profile,
        List<ProfileValidationError> errors)
    {
        var squadIds = profile.Wings
            .SelectMany(wing => wing.Squads)
            .Select(squad => squad.Id)
            .ToHashSet();
        var fallbackCount = 0;
        var ruleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var ruleIndex = 0; ruleIndex < profile.ShipRules.Count; ruleIndex++)
        {
            var rule = profile.ShipRules[ruleIndex];
            var rulePath = $"profile.shipRules[{ruleIndex}]";
            var shipTypes = ShipRuleResolver.ParseShipTypes(rule.ShipTypeName);
            var ruleKey = rule.IsFallback
                ? "<fallback>"
                : string.Join(",", shipTypes.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            if (!ruleKeys.Add(ruleKey))
            {
                errors.Add(new(
                    "ship_rule.match.duplicate",
                    "Two ship rules have the same match set. Combine them or change their priority targets.",
                    rulePath));
            }

            if (rule.IsFallback)
            {
                fallbackCount++;
                if (fallbackCount > 1)
                {
                    errors.Add(new(
                        "ship_rule.fallback.duplicate",
                        "A profile can have only one fallback ship rule.",
                        rulePath));
                }

                if (ruleIndex != profile.ShipRules.Count - 1)
                {
                    errors.Add(new(
                        "ship_rule.fallback.order",
                        "The fallback ship rule must be last so it cannot hide later policies.",
                        rulePath));
                }
            }
            else if (shipTypes.Length == 0)
            {
                errors.Add(new(
                    "ship_rule.ship.required",
                    "Add one or more exact ship types, separated by commas.",
                    $"{rulePath}.shipTypeName"));
            }

            if (rule.MaximumPerSquad is < 1 or > MaximumCharactersPerSquad)
            {
                errors.Add(new(
                    "ship_rule.capacity.invalid",
                    $"Ship-rule capacity must be between 1 and {MaximumCharactersPerSquad}.",
                    $"{rulePath}.maximumPerSquad"));
            }

            if (!squadIds.Contains(rule.TargetSquadId))
            {
                errors.Add(new(
                    "ship_rule.squad.missing",
                    $"The rule for '{rule.ShipTypeName}' targets a squad that is not in this profile.",
                    $"{rulePath}.targetSquadId"));
            }

            if (rule.OverflowSquadId is Guid overflowSquadId &&
                (!squadIds.Contains(overflowSquadId) || overflowSquadId == rule.TargetSquadId))
            {
                errors.Add(new(
                    "ship_rule.overflow.invalid",
                    "Choose a different existing squad for overflow/balancing.",
                    $"{rulePath}.overflowSquadId"));
            }
        }
    }
}

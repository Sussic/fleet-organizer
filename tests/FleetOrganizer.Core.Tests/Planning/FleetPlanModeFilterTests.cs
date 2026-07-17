using FleetOrganizer.Core.Planning;

namespace FleetOrganizer.Core.Tests.Planning;

public sealed class FleetPlanModeFilterTests
{
    private static readonly FleetPlanItem[] MixedItems =
    [
        new(FleetPlanItemKind.CreateWing, "Create wing", "detail"),
        new(FleetPlanItemKind.InviteCharacter, "Invite", "detail", CharacterId: 1),
        new(FleetPlanItemKind.MoveCharacter, "Move", "detail", CharacterId: 2),
        new(FleetPlanItemKind.ChangeRole, "Promote", "detail", CharacterId: 3),
    ];

    [Theory]
    [InlineData(FleetRunMode.InviteMissing, FleetPlanItemKind.InviteCharacter)]
    [InlineData(FleetRunMode.FixStructure, FleetPlanItemKind.CreateWing)]
    public void QuickModeKeepsOnlyItsSafeAction(
        FleetRunMode mode,
        FleetPlanItemKind expectedKind)
    {
        var plan = CreatePlan(MixedItems);

        var filtered = FleetPlanModeFilter.Apply(plan, mode);

        var item = Assert.Single(filtered.Items, candidate => candidate.Kind != FleetPlanItemKind.Blocked);
        Assert.Equal(expectedKind, item.Kind);
        Assert.Equal(mode, filtered.Mode);
    }

    [Fact]
    public void PlacementIsBlockedUntilTargetStructureExists()
    {
        var plan = CreatePlan(MixedItems);

        var filtered = FleetPlanModeFilter.Apply(plan, FleetRunMode.PlacePresent);

        Assert.Contains(filtered.Items, item =>
            item.Kind == FleetPlanItemKind.Blocked &&
            item.Title == "Target structure is not ready");
    }

    [Fact]
    public void CommanderOnlyIsBlockedUntilPlacementIsReady()
    {
        var plan = CreatePlan(
        [
            new(FleetPlanItemKind.MoveCharacter, "Move", "detail", CharacterId: 2),
            new(FleetPlanItemKind.ChangeRole, "Promote", "detail", CharacterId: 2),
        ]);

        var filtered = FleetPlanModeFilter.Apply(plan, FleetRunMode.AssignCommanders);

        Assert.Contains(filtered.Items, item =>
            item.Kind == FleetPlanItemKind.Blocked &&
            item.Title == "Commander placement is not ready");
    }

    private static FleetDryRunPlan CreatePlan(FleetPlanItem[] items) =>
        new(Guid.NewGuid(), "Test", 123, "Boss", items, 0);
}

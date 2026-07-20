using System.Collections.ObjectModel;
using FleetOrganizer.App.ViewModels;

namespace FleetOrganizer.Infrastructure.Tests.Ui;

public sealed class LiveFleetBoardProjectionTests
{
    [Fact]
    public void FlattenPreservesEveHierarchyOrderAndOmitsFilteredRows()
    {
        var visibleMember = Member(7, "Visible Pilot");
        var hiddenMember = Member(8, "Hidden Pilot");
        hiddenMember.IsVisible = false;
        var visibleSquad = new LiveFleetBoardSquadViewModel(
            100,
            200,
            "Wing 1",
            "Squad 1",
            isLiveEmpty: false,
            new ObservableCollection<LiveFleetBoardMemberViewModel>([visibleMember, hiddenMember]));
        var filteredSquad = new LiveFleetBoardSquadViewModel(
            100,
            201,
            "Wing 1",
            "Squad 2",
            isLiveEmpty: true,
            []);
        filteredSquad.IsVisible = false;
        var wing = new LiveFleetBoardWingViewModel(
            100,
            "Wing 1",
            isLiveEmpty: false,
            new ObservableCollection<LiveFleetBoardSquadViewModel>([visibleSquad, filteredSquad]));

        var rows = LiveFleetBoardProjection.Flatten([wing]);

        Assert.Collection(
            rows,
            row =>
            {
                Assert.True(row.IsWing);
                Assert.Same(wing, row.Wing);
            },
            row =>
            {
                Assert.True(row.IsSquad);
                Assert.Same(visibleSquad, row.Squad);
            },
            row =>
            {
                Assert.True(row.IsMember);
                Assert.Same(visibleMember, row.Member);
                Assert.Same(visibleSquad, row.Squad);
            });
    }

    [Fact]
    public void FlattenReturnsNoRowsForAFilteredWing()
    {
        var wing = new LiveFleetBoardWingViewModel(100, "Wing 1", true, []);
        wing.IsVisible = false;

        Assert.Empty(LiveFleetBoardProjection.Flatten([wing]));
    }

    private static LiveFleetBoardMemberViewModel Member(long id, string name) =>
        new(
            id,
            name,
            "squad_member",
            "Squad Member",
            "Scimitar",
            100,
            200,
            "Wing 1",
            "Squad 1",
            stagedMove: null);
}

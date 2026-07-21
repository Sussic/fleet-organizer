namespace FleetOrganizer.App.ViewModels;

public sealed class LiveFleetSelectionModel
{
    private long? anchorCharacterId;

    public void Select(
        IReadOnlyList<LiveFleetBoardMemberViewModel> allMembers,
        long characterId,
        bool extendRange,
        bool toggle)
    {
        ArgumentNullException.ThrowIfNull(allMembers);
        var visible = allMembers.Where(member => member.IsVisible && member.CanStage).ToArray();
        var clickedIndex = Array.FindIndex(visible, member => member.CharacterId == characterId);
        if (clickedIndex < 0)
        {
            return;
        }

        if (extendRange && anchorCharacterId is long anchorId)
        {
            var anchorIndex = Array.FindIndex(visible, member => member.CharacterId == anchorId);
            if (anchorIndex >= 0)
            {
                if (!toggle)
                {
                    Clear(allMembers, resetAnchor: false);
                }

                var start = Math.Min(anchorIndex, clickedIndex);
                var end = Math.Max(anchorIndex, clickedIndex);
                for (var index = start; index <= end; index++)
                {
                    visible[index].IsSelected = true;
                }

                return;
            }
        }

        if (toggle)
        {
            visible[clickedIndex].IsSelected = !visible[clickedIndex].IsSelected;
        }
        else
        {
            foreach (var member in allMembers)
            {
                member.IsSelected = member.CharacterId == characterId;
            }
        }

        anchorCharacterId = characterId;
    }

    public static void SelectAllVisible(IEnumerable<LiveFleetBoardMemberViewModel> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        foreach (var member in members.Where(member => member.IsVisible && member.CanStage))
        {
            member.IsSelected = true;
        }
    }

    public void Clear(IEnumerable<LiveFleetBoardMemberViewModel> members) =>
        Clear(members, resetAnchor: true);

    public void ResetAnchor() => anchorCharacterId = null;

    private void Clear(
        IEnumerable<LiveFleetBoardMemberViewModel> members,
        bool resetAnchor)
    {
        foreach (var member in members)
        {
            member.IsSelected = false;
        }

        if (resetAnchor)
        {
            anchorCharacterId = null;
        }
    }
}

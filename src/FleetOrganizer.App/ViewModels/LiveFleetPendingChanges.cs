using System.Collections.ObjectModel;

namespace FleetOrganizer.App.ViewModels;

public sealed class LiveFleetPendingChanges
{
    private readonly List<object> queuedOrder = [];

    public ObservableCollection<StagedLiveMoveViewModel> Moves { get; } = [];

    public ObservableCollection<StagedLiveInviteViewModel> Invites { get; } = [];

    public ObservableCollection<StagedLiveStructureChangeViewModel> StructureChanges { get; } = [];

    public event EventHandler? Changed;

    public int QueuedCount => Moves.Count + StructureChanges.Count;

    public bool HasQueuedChanges => QueuedCount > 0;

    public void AddMove(StagedLiveMoveViewModel move)
    {
        ArgumentNullException.ThrowIfNull(move);
        var existing = Moves.FirstOrDefault(candidate => candidate.CharacterId == move.CharacterId);
        if (existing is not null)
        {
            RemoveFromOrder(existing);
            Moves.Remove(existing);
        }

        Moves.Add(move);
        queuedOrder.Add(move);
        OnChanged();
    }

    public void AddInvite(StagedLiveInviteViewModel invite)
    {
        ArgumentNullException.ThrowIfNull(invite);
        Invites.Add(invite);
        OnChanged();
    }

    public void AddStructureChange(StagedLiveStructureChangeViewModel change)
    {
        ArgumentNullException.ThrowIfNull(change);
        StructureChanges.Add(change);
        queuedOrder.Add(change);
        OnChanged();
    }

    public bool RemoveMove(StagedLiveMoveViewModel move) => Remove(Moves, move, trackOrder: true);

    public bool RemoveInvite(StagedLiveInviteViewModel invite) => Remove(Invites, invite, trackOrder: false);

    public bool RemoveStructureChange(StagedLiveStructureChangeViewModel change) =>
        Remove(StructureChanges, change, trackOrder: true);

    public string? UndoLastQueuedChange()
    {
        while (queuedOrder.Count > 0)
        {
            var index = queuedOrder.Count - 1;
            var item = queuedOrder[index];
            queuedOrder.RemoveAt(index);
            switch (item)
            {
                case StagedLiveMoveViewModel move when Moves.Remove(move):
                    OnChanged();
                    return move.Summary;
                case StagedLiveStructureChangeViewModel change when StructureChanges.Remove(change):
                    OnChanged();
                    return change.Summary;
            }
        }

        return null;
    }

    public void ClearQueued()
    {
        Moves.Clear();
        StructureChanges.Clear();
        queuedOrder.Clear();
        OnChanged();
    }

    public void Reset()
    {
        Moves.Clear();
        Invites.Clear();
        StructureChanges.Clear();
        queuedOrder.Clear();
        OnChanged();
    }

    private bool Remove<T>(ObservableCollection<T> collection, T item, bool trackOrder)
        where T : class
    {
        if (!collection.Remove(item))
        {
            return false;
        }

        if (trackOrder)
        {
            RemoveFromOrder(item);
        }

        OnChanged();
        return true;
    }

    private void RemoveFromOrder(object item)
    {
        for (var index = queuedOrder.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(queuedOrder[index], item) || Equals(queuedOrder[index], item))
            {
                queuedOrder.RemoveAt(index);
                return;
            }
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

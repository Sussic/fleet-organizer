namespace FleetOrganizer.App.ViewModels;

public sealed record LiveFleetTreeNodeViewModel(
    string Title,
    string Detail,
    LiveFleetTreeNodeViewModel[] Children);

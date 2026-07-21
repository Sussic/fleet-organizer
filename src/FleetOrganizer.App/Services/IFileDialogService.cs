namespace FleetOrganizer.App.Services;

public interface IFileDialogService
{
    string? ChooseSavePath(string title, string fileName, string filter, string defaultExtension);

    string? ChooseOpenPath(string title, string filter);
}

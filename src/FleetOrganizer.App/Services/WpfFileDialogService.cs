using Microsoft.Win32;

namespace FleetOrganizer.App.Services;

public sealed class WpfFileDialogService : IFileDialogService
{
    public string? ChooseSavePath(
        string title,
        string fileName,
        string filter,
        string defaultExtension)
    {
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = defaultExtension,
            FileName = fileName,
            Filter = filter,
            Title = title,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseOpenPath(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = filter,
            Multiselect = false,
            Title = title,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

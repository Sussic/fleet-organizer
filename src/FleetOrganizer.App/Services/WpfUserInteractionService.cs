using System.Windows;

namespace FleetOrganizer.App.Services;

public sealed class WpfUserInteractionService : IUserInteractionService
{
    public bool Confirm(string title, string message, UserConfirmationKind kind) =>
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            kind == UserConfirmationKind.Warning
                ? MessageBoxImage.Warning
                : MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Inform(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}

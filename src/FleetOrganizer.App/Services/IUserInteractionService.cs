namespace FleetOrganizer.App.Services;

public enum UserConfirmationKind
{
    Question,
    Warning,
}

public interface IUserInteractionService
{
    bool Confirm(string title, string message, UserConfirmationKind kind);

    void Inform(string title, string message);
}

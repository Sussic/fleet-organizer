namespace FleetOrganizer.Core.Profiles;

public sealed record ProfileValidationError(
    string Code,
    string Message,
    string Path);

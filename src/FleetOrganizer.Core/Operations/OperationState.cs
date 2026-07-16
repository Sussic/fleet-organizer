namespace FleetOrganizer.Core.Operations;

public enum OperationState
{
    DetectFleet = 0,
    ReadCurrentState = 1,
    EnsureStructure = 2,
    ResolveRoster = 3,
    InviteMissing = 4,
    AwaitAcceptance = 5,
    PlaceMembers = 6,
    AssignCommanders = 7,
    Verify = 8,
    Complete = 9,
    NeedsAttention = 10,
    Cancelled = 11,
}

namespace NoCTF.Domain.Notifications;

/// <summary>Typed action encoded at the existing notification ActionValue storage boundary.</summary>
public enum CompetitionAnnouncementChangeAction : short
{
    Edit = 1,
    Withdraw = 2
}

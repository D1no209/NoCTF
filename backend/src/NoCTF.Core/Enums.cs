namespace NoCTF.Core;

public enum UserRole
{
    Admin,
    Organizer,
    User
}

public enum TeamMemberRole
{
    Captain,
    Member
}

public enum CollaboratorRole
{
    Manager,
    Observer
}

public enum CompetitionStatus
{
    Draft,
    Published,
    Running,
    Paused,
    Finished
}

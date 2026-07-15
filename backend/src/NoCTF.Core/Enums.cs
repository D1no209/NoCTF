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

public enum TeamRegistrationStatus
{
    Pending,
    Approved,
    Rejected
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

public enum BackgroundTaskStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Retrying,
    Cancelled
}

public enum QqBotEventType
{
    CompetitionStarted,
    ChallengePublished,
    HintPublished,
    FirstBlood,
    SecondBlood,
    ThirdBlood,
    TeamPenalized,
    Announcement
}

public enum QqBotEventStatus
{
    Pending,
    Expanded,
    Suppressed,
    Failed
}

public enum QqBotDeliveryStatus
{
    Pending,
    Leased,
    Retrying,
    Succeeded,
    Failed,
    Cancelled
}

public enum QqBotDeliverySource
{
    Automatic,
    Manual,
    Test,
    ManualRetry
}

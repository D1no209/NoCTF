namespace NoCTF.Domain.Shared;

public enum EntityReferenceKind : short
{
    User,
    Competition,
    Team,
    Challenge,
    CompetitionChallenge,
    ChallengeHint,
    Submission,
    ScoringEvent,
    RuntimeInstance,
    DataExport,
    File,
    CompetitionEvent,
    Notification,
    Platform
}

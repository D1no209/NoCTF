namespace NoCTF.Domain.Shared;

public enum EntityReferenceKind : short
{
    User,
    Competition,
    Team,
    Challenge,
    CompetitionChallenge,
    ChallengeHint,
    GameplayFact,
    RuntimeInstance,
    File,
    CompetitionEvent,
    Notification,
    Platform
}

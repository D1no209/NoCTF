namespace NoCTF.Domain.Challenges.Questions;

public enum CompetitionQuestionEntryKind : short
{
    Message,
    StatusTransition
}

public enum CompetitionQuestionParticipantRole : short
{
    Asker,
    Handler,
    Participant,
    Judge,
    ChallengeOwner,
    CompetitionManager,
    PlatformAdministrator
}

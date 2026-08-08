namespace NoCTF.Domain.Challenges.Questions;

public enum CompetitionQuestionSubject : short
{
    Challenge,
    Platform
}

public enum CompetitionQuestionStatus : short
{
    Pending,
    Replied,
    Resolved,
    Closed
}

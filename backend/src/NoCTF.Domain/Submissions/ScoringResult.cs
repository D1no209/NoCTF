namespace NoCTF.Domain.Submissions;

public enum ScoringResult
{
    Correct,
    Wrong,
    Duplicate,
    AttemptsExhausted,
    PlatformFailed,
    Rejected
}

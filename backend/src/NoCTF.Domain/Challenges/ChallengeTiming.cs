using NoCTF.Domain.Gameplay;

namespace NoCTF.Domain.Challenges;

public enum ChallengeOpeningState : short { None, Pending, Applied, Canceled }
public enum GameplayFactTimeEligibility : short { Valid, NotOpened, SubmissionClosed }
public enum ChallengeTimingPhase : short { NotOpened, Scoring, JudgementOnly, SubmissionClosed }

public sealed record ChallengeTiming(DateTimeOffset? AutoOpenAt = null,
    DateTimeOffset? ScoringEndsAt = null, DateTimeOffset? SubmissionDeadlineAt = null)
{
    public bool IsEmpty => AutoOpenAt is null && ScoringEndsAt is null && SubmissionDeadlineAt is null;
    public bool IsValid => (AutoOpenAt is null || ScoringEndsAt is null || AutoOpenAt <= ScoringEndsAt)
        && (AutoOpenAt is null || SubmissionDeadlineAt is null || AutoOpenAt <= SubmissionDeadlineAt)
        && (ScoringEndsAt is null || SubmissionDeadlineAt is null || ScoringEndsAt <= SubmissionDeadlineAt);

    public GameplayFactTimeEligibility Eligibility(DateTimeOffset occurredAt, bool practice = false) =>
        AutoOpenAt is { } opening && occurredAt < opening ? GameplayFactTimeEligibility.NotOpened
        : !practice && SubmissionDeadlineAt is { } closing && occurredAt >= closing
            ? GameplayFactTimeEligibility.SubmissionClosed : GameplayFactTimeEligibility.Valid;

    public bool CanScore(DateTimeOffset occurredAt, bool practice = false) => !practice
        && Eligibility(occurredAt) == GameplayFactTimeEligibility.Valid
        && (ScoringEndsAt is null || occurredAt < ScoringEndsAt);
    public ChallengeTimingPhase Phase(DateTimeOffset now, bool practice = false) => Eligibility(now, practice) switch
    {
        GameplayFactTimeEligibility.NotOpened => ChallengeTimingPhase.NotOpened,
        GameplayFactTimeEligibility.SubmissionClosed => ChallengeTimingPhase.SubmissionClosed,
        _ => CanScore(now, practice) ? ChallengeTimingPhase.Scoring : ChallengeTimingPhase.JudgementOnly
    };

    public bool CanScoreCheckpoint(DateTimeOffset occurredAt) =>
        (AutoOpenAt is null || occurredAt >= AutoOpenAt)
        && (ScoringEndsAt is null || occurredAt < ScoringEndsAt);

    public bool CanScoreInterval(DateTimeOffset start, DateTimeOffset end) => start < end
        && (AutoOpenAt is null || start >= AutoOpenAt)
        && (ScoringEndsAt is null || end <= ScoringEndsAt);

    public GameplayFactResult Classify(GameplayFactResult result, DateTimeOffset occurredAt, bool practice = false) =>
        result is GameplayFactResult.Correct or GameplayFactResult.RightButDue
            ? CanScore(occurredAt, practice) ? GameplayFactResult.Correct : GameplayFactResult.RightButDue
            : result;

    public static ChallengeTiming From(CompetitionChallenge challenge) =>
        new(challenge.AutoOpenAt, challenge.ScoringEndsAt, challenge.SubmissionDeadlineAt);
}

public static class GameplayFactCompletion
{
    public static bool IsSuccessful(GameplayFactResult? result, GameplayFactTimeEligibility eligibility) =>
        eligibility == GameplayFactTimeEligibility.Valid
        && result is GameplayFactResult.Correct or GameplayFactResult.RightButDue;
}

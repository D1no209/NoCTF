using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Scoring.Leaderboard;

public sealed record ScoreboardSlotDetailFact(
    Guid Id,
    GameplayFactKind Kind,
    GameplayFactState State,
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode,
    GameplayFactReferenceKind? ReferenceKind,
    Guid? ReferenceId,
    Guid? ActorUserId,
    string? ActorDisplayName,
    Guid? VictimTeamId,
    bool ScoringIdentityKnown,
    DateTimeOffset OccurredAt);

public sealed record ScoreboardSlotDetailQuery(
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    GameMode Mode,
    Guid? RoundId,
    DateTimeOffset? RoundStartAt,
    DateTimeOffset? RoundEndAt,
    DateTimeOffset DataAsOf,
    DateTimeOffset? BeforeOccurredAt,
    Guid? BeforeId,
    int Limit);

public sealed record ScoreboardAdjustmentDetailFact(
    Guid Id,
    Guid? ActorUserId,
    string? ActorDisplayName,
    DateTimeOffset OccurredAt,
    long Delta);

public sealed record ScoreboardAdjustmentDetailQuery(
    Guid CompetitionId,
    Guid TeamId,
    DateTimeOffset DataAsOf,
    DateTimeOffset? BeforeOccurredAt,
    Guid? BeforeId,
    int Limit);

public interface IScoreboardDetailReader
{
    Task<IReadOnlyList<ScoreboardSlotDetailFact>> ReadSlotAsync(
        ScoreboardSlotDetailQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ScoreboardAdjustmentDetailFact>> ReadAdjustmentsAsync(
        ScoreboardAdjustmentDetailQuery query,
        CancellationToken cancellationToken);
}

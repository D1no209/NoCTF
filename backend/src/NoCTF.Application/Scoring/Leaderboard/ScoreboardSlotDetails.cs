using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Scoring.Leaderboard;

public sealed record ScoreboardSlotDetailFact(
    Guid Id,
    GameplayFactKind Kind,
    GameplayFactState State,
    GameplayFactResult? Result,
    Guid? ActorUserId,
    string? ActorDisplayName,
    Guid? VictimTeamId,
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

public interface IScoreboardSlotDetailReader
{
    Task<IReadOnlyList<ScoreboardSlotDetailFact>> ReadAsync(
        ScoreboardSlotDetailQuery query,
        CancellationToken cancellationToken);
}

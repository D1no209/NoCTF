using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class ScoreboardSlotDetailReader(NoCtfDbContext db) : IScoreboardSlotDetailReader
{
    public async Task<IReadOnlyList<ScoreboardSlotDetailFact>> ReadAsync(
        ScoreboardSlotDetailQuery query,
        CancellationToken cancellationToken)
    {
        var facts = db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == query.CompetitionId
                && fact.TeamId == query.TeamId
                && fact.CompetitionChallengeId == query.CompetitionChallengeId
                && fact.OccurredAt <= query.DataAsOf);

        if (query.Mode == GameMode.Awd && query.RoundId is Guid roundId)
        {
            facts = facts.Where(fact => fact.ReferenceKind == GameplayFactReferenceKind.AwdRound
                && fact.ReferenceId == roundId);
        }
        else if (query.RoundStartAt is DateTimeOffset startsAt
                 && query.RoundEndAt is DateTimeOffset endsAt)
        {
            facts = facts.Where(fact => fact.OccurredAt >= startsAt && fact.OccurredAt < endsAt);
        }

        if (query.BeforeOccurredAt is DateTimeOffset beforeAt && query.BeforeId is Guid beforeId)
        {
            facts = facts.Where(fact => fact.OccurredAt < beforeAt
                || fact.OccurredAt == beforeAt && fact.Id.CompareTo(beforeId) < 0);
        }

        return await facts
            .OrderByDescending(fact => fact.OccurredAt)
            .ThenByDescending(fact => fact.Id)
            .Select(fact => new ScoreboardSlotDetailFact(
                fact.Id,
                fact.Kind,
                fact.State,
                fact.Result,
                fact.ActorUserId,
                fact.ActorUserId == null
                    ? null
                    : db.Users
                        .Where(user => user.Id == fact.ActorUserId.Value)
                        .Select(user => user.UserName)
                        .FirstOrDefault(),
                fact.VictimTeamId,
                fact.OccurredAt))
            .Take(query.Limit)
            .ToArrayAsync(cancellationToken);
    }
}

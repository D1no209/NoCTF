using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class ScoreboardTrendFactReader(NoCtfDbContext db) : IScoreboardTrendFactReader
{
    public async Task<IReadOnlyList<ScoreboardTrendAdjustment>> ReadTheoryAdjustmentsAsync(Guid competitionId,
        IReadOnlyList<Guid> teamIds, DateTimeOffset cutoff, CancellationToken ct)
    {
        var activityId = await db.CompetitionTheoryActivities.AsNoTracking().Where(link => link.CompetitionId == competitionId)
            .Select(link => (Guid?)link.ActivityId).SingleOrDefaultAsync(ct);
        if (activityId is null || teamIds.Count == 0) return [];
        var activity = await db.TheoryActivities.AsNoTracking().SingleAsync(item => item.Id == activityId, ct);
        if (activity.State == NoCTF.Domain.Theory.Activities.TheoryActivityState.Voided) return [];
        var participants = await db.TheoryParticipants.AsNoTracking().Where(item => item.ActivityId == activityId
                && item.TeamId != null && teamIds.Contains(item.TeamId.Value))
            .ToDictionaryAsync(item => item.Id, item => item.TeamId!.Value, ct);
        var ids = participants.Keys.ToArray();
        var papers = await db.TheoryPapers.AsNoTracking().Where(paper => ids.Contains(paper.ParticipantId)
                && paper.State == NoCTF.Domain.Theory.Papers.TheoryPaperState.Submitted && paper.SubmittedAt <= cutoff)
            .Select(paper => new { paper.Id, paper.ParticipantId, paper.Attempt, At = paper.SubmittedAt!.Value,
                Score = paper.Items.Where(item => item.Result == NoCTF.Domain.Theory.Papers.TheoryAnswerResult.Correct
                    || item.Result == NoCTF.Domain.Theory.Papers.TheoryAnswerResult.FullCredit).Sum(item => (long?)item.Points) ?? 0 })
            .ToArrayAsync(ct);
        var result = new List<ScoreboardTrendAdjustment>();
        foreach (var group in papers.GroupBy(paper => paper.ParticipantId))
        {
            var prefix = new List<NoCTF.Application.Theory.Papers.TheoryPaperScore>();
            var previous = 0L;
            foreach (var paper in group.OrderBy(paper => paper.At).ThenBy(paper => paper.Attempt))
            {
                prefix.Add(new(paper.Id, paper.Attempt, paper.Score, paper.At));
                var score = NoCTF.Application.Theory.Papers.TheoryPaperGrading.Aggregate(activity.ScoringMode, prefix).Points;
                result.Add(new(paper.Id, participants[paper.ParticipantId], paper.At, checked(score - previous)));
                previous = score;
            }
        }
        return result;
    }
    public async Task<IReadOnlyList<ScoreboardTrendAdjustment>> ReadManualAdjustmentsAsync(
        Guid competitionId,
        IReadOnlyList<Guid> teamIds,
        DateTimeOffset dataAsOf,
        CancellationToken cancellationToken)
    {
        if (teamIds.Count == 0)
            return [];

        var rows = await db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId
                && fact.TeamId != null
                && teamIds.Contains(fact.TeamId.Value)
                && fact.Kind == GameplayFactKind.ManualAdjustment
                && fact.State == GameplayFactState.Completed
                && fact.Result == GameplayFactResult.Applied
                && fact.OccurredAt <= dataAsOf)
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.Id)
            .Select(fact => new
            {
                fact.Id,
                TeamId = fact.TeamId!.Value,
                fact.OccurredAt,
                fact.Value
            })
            .ToArrayAsync(cancellationToken);

        return rows.Select(row => new ScoreboardTrendAdjustment(
            row.Id,
            row.TeamId,
            row.OccurredAt,
            long.Parse(row.Value!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)))
            .ToArray();
    }
}

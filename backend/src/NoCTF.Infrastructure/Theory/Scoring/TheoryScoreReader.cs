using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Theory.Papers;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Theory.Scoring;

internal sealed record TheoryScoreData(IReadOnlyList<LeaderboardTheoryScore>? Scores, IReadOnlyList<TheoryProjectionReceipt> Receipts);
internal static class TheoryScoreReader
{
    internal static async Task<TheoryScoreData> ReadAsync(NoCtfDbContext db, Guid competitionId, DateTimeOffset cutoff, CancellationToken ct)
    {
        var activityId = await db.CompetitionTheoryActivities.AsNoTracking().Where(link => link.CompetitionId == competitionId)
            .Select(link => (Guid?)link.ActivityId).SingleOrDefaultAsync(ct);
        if (activityId is null) return new(null, []);
        var activity = await db.TheoryActivities.AsNoTracking().SingleAsync(activity => activity.Id == activityId, ct);
        if (activity.State == TheoryActivityState.Draft) return new(null, []);
        var participants = await db.TheoryParticipants.AsNoTracking().Where(participant => participant.ActivityId == activityId && participant.TeamId != null)
            .Select(participant => new { participant.Id, TeamId = participant.TeamId!.Value }).ToArrayAsync(ct);
        var ids = participants.Select(participant => participant.Id).ToArray();
        var papers = await db.TheoryPapers.AsNoTracking().Where(paper => ids.Contains(paper.ParticipantId)
                && (paper.State == TheoryPaperState.Submitted || paper.State == TheoryPaperState.Voided) && paper.SubmittedAt <= cutoff)
            .Select(paper => new { paper.Id, paper.ParticipantId, paper.Attempt, paper.SubmittedAt,
                paper.State,
                Receipt = db.GameplayFacts.Where(fact => fact.TheoryPaperId == paper.Id && fact.State == NoCTF.Domain.Gameplay.GameplayFactState.Queued)
                    .Select(fact => new TheoryProjectionReceipt(fact.Id, fact.ConcurrencyStamp)).SingleOrDefault(),
                Score = paper.Items.Where(item => item.Result == TheoryAnswerResult.Correct || item.Result == TheoryAnswerResult.FullCredit)
                    .Sum(item => (long?)item.Points) ?? 0 }).ToArrayAsync(ct);
        var receipts = papers.Where(paper => paper.Receipt != null).Select(paper => paper.Receipt!).ToArray();
        if (activity.State == TheoryActivityState.Voided) return new([], receipts);
        return new(participants.Select(participant =>
        {
            var effective = TheoryPaperGrading.Aggregate(activity.ScoringMode, papers.Where(paper => paper.ParticipantId == participant.Id && paper.State == TheoryPaperState.Submitted)
                .Select(paper => new TheoryPaperScore(paper.Id, paper.Attempt, paper.Score, paper.SubmittedAt!.Value)).ToArray());
            return new LeaderboardTheoryScore(participant.TeamId, effective.Points, effective.AchievedAt, effective.HasSubmission);
        }).ToArray(), receipts);
    }
}

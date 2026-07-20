using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Submissions.Processing;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfSystemScoringEventProcessor(
    NoCtfDbContext db,
    ILeaderboardCache cache,
    IBackgroundWorkScheduler scheduler) : ISystemScoringEventProcessor
{
    public async Task ProcessAsync(Guid scoringEventId, CancellationToken cancellationToken)
    {
        var competitionId = await db.ScoringEvents.AsNoTracking()
            .Where(scoringEvent => scoringEvent.Id == scoringEventId && scoringEvent.SubmissionId == null)
            .Select(scoringEvent => (Guid?)scoringEvent.CompetitionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (competitionId is not Guid id) return;

        await cache.InvalidateAsync(id, cancellationToken);
        await scheduler.EnqueueLeaderboardRefreshAsync(id, cancellationToken);
    }
}

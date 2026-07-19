using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Maintenance;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

/// <summary>Retires current scoring facts in stable order and requeues submissions for replacement facts.</summary>
public sealed class EfCompetitionRebuildProcessor(
    NoCtfDbContext db,
    IBackgroundWorkScheduler scheduler) : ICompetitionRebuildProcessor
{
    public async Task RebuildAsync(Guid competitionId, CancellationToken ct)
    {
        var submissionIds = await db.Submissions.AsNoTracking()
            .Where(submission => submission.CompetitionId == competitionId
                                 && submission.ScoringEventId != null)
            .OrderBy(submission => submission.ReceivedAt)
            .ThenBy(submission => submission.Id)
            .Select(submission => submission.Id)
            .ToListAsync(ct);

        foreach (var submissionId in submissionIds)
        {
            var retired = await RetireCurrentEventAsync(submissionId, ct);
            if (retired)
                await scheduler.EnqueueSubmissionAsync(submissionId, ct);
        }

        // System facts are immutable observations; their stable OccurredAt + Id order is
        // consumed by the projector and therefore does not require a replacement row.
        _ = await db.ScoringEvents.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId && item.SubmissionId == null)
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .ToListAsync(ct);
    }

    private async Task<bool> RetireCurrentEventAsync(Guid submissionId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var submission = await db.Submissions
            .Include(item => item.ScoringEvent)
            .SingleOrDefaultAsync(item => item.Id == submissionId, ct);
        if (submission?.ScoringEvent is null)
            return false;

        var now = DateTimeOffset.UtcNow;
        submission.ScoringEvent.IsDeleted = true;
        submission.ScoringEvent.DeletedAt = now;
        submission.ScoringEvent.DeletedById = null;
        submission.ScoringEvent.RowVersion++;
        submission.ScoringEventId = null;
        submission.ProcessingVersion++;
        submission.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}

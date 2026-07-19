using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfSystemScoringEventStore(NoCtfDbContext db) : ISystemScoringEventStore
{
    public async Task<RecordSystemScoringEventResult> RecordAsync(RecordSystemScoringEventCommand command, CancellationToken ct)
    {
        var existing = await db.ScoringEvents.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompetitionId == command.CompetitionId && x.Kind == command.Kind && x.SourceKey == command.SourceKey, ct);
        if (existing is not null) return new(existing.Id, false);

        var now = DateTimeOffset.UtcNow;
        var entity = new ScoringEvent
        {
            Id = Guid.CreateVersion7(now),
            CompetitionId = command.CompetitionId,
            TeamId = command.TeamId,
            ChallengeId = command.ChallengeId,
            SubmissionId = null,
            Kind = command.Kind,
            Result = command.Result,
            FailureCode = command.FailureCode,
            OccurredAt = command.OccurredAt,
            ProcessedAt = now,
            EvaluatorVersion = command.EvaluatorVersion,
            SourceKey = command.SourceKey,
            CreatedAt = now
        };
        db.ScoringEvents.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            return new(entity.Id, true);
        }
        catch (DbUpdateException)
        {
            db.Entry(entity).State = EntityState.Detached;
            existing = await db.ScoringEvents.AsNoTracking().SingleAsync(
                x => x.CompetitionId == command.CompetitionId && x.Kind == command.Kind && x.SourceKey == command.SourceKey, ct);
            return new(existing.Id, false);
        }
    }
}

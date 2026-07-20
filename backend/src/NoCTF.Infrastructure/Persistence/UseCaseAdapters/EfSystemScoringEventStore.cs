using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Competitions;
using Npgsql;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfSystemScoringEventStore(NoCtfDbContext db) : ISystemScoringEventStore
{
    public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking().Where(item => item.Id == competitionId && !item.Deletion.IsDeleted)
            .Select(item => (CompetitionStatus?)item.Status).SingleOrDefaultAsync(ct);

    public Task<GameMode?> GetCompetitionModeAsync(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking().Where(item => item.Id == competitionId && !item.Deletion.IsDeleted)
            .Select(item => (GameMode?)item.Mode).SingleOrDefaultAsync(ct);

    public async Task<RecordSystemScoringEventResult> RecordAsync(RecordSystemScoringEventCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        if (status is null)
            return new(Guid.Empty, false, SystemScoringEventRecordFailure.CompetitionNotFound);
        if (status == CompetitionStatus.Finished)
            return new(Guid.Empty, false, SystemScoringEventRecordFailure.CompetitionFinished);
        var existing = await db.ScoringEvents.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompetitionId == command.CompetitionId && x.Kind == command.Kind && x.SourceKey == command.SourceKey, ct);
        if (existing is not null)
        {
            await transaction.CommitAsync(ct);
            return SameFact(existing, command)
                ? new(existing.Id, false)
                : new(Guid.Empty, false, SystemScoringEventRecordFailure.SourceConflict);
        }

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
            await transaction.CommitAsync(ct);
            return new(entity.Id, true);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_scoring_events_CompetitionId_Kind_SourceKey"
        })
        {
            await transaction.RollbackAsync(ct);
            db.Entry(entity).State = EntityState.Detached;
            existing = await db.ScoringEvents.AsNoTracking().SingleAsync(
                x => x.CompetitionId == command.CompetitionId && x.Kind == command.Kind && x.SourceKey == command.SourceKey, ct);
            return SameFact(existing, command)
                ? new(existing.Id, false)
                : new(Guid.Empty, false, SystemScoringEventRecordFailure.SourceConflict);
        }
    }

    private static bool SameFact(ScoringEvent existing, RecordSystemScoringEventCommand command) =>
        existing.TeamId == command.TeamId
        && existing.ChallengeId == command.ChallengeId
        && existing.Result == command.Result
        && existing.FailureCode == command.FailureCode
        && existing.OccurredAt == command.OccurredAt
        && existing.EvaluatorVersion == command.EvaluatorVersion;
}

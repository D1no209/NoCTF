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
            CompetitionChallengeId = command.CompetitionChallengeId,
            StageId = command.StageId,
            ChallengeInstanceId = command.ChallengeInstanceId,
            SubmissionId = command.SubmissionId,
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
        if (command.Kind == ScoringEventKind.AwdpFixCheck && command.SubmissionId is Guid submissionId)
        {
            var verification = await db.FixSubmissionRecords.SingleOrDefaultAsync(
                item => item.SubmissionId == submissionId && item.VerificationStatus == FixVerificationStatus.Verifying, ct);
            if (verification is null)
                return new(Guid.Empty, false, SystemScoringEventRecordFailure.SourceConflict);
            verification.VerificationStatus = command.Result switch
            {
                ScoringResult.Correct => FixVerificationStatus.Valid,
                ScoringResult.PlatformFailed => FixVerificationStatus.PlatformFailed,
                _ => FixVerificationStatus.TeamFailure
            };
            verification.FailureCategory = command.FailureCode;
            verification.VerifiedAt = command.OccurredAt;
            verification.VerifierVersion = command.EvaluatorVersion;
            verification.UpdatedAt = now;
            verification.RowVersion++;
        }
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
        && existing.CompetitionChallengeId == command.CompetitionChallengeId
        && existing.SubmissionId == command.SubmissionId
        && existing.StageId == command.StageId
        && existing.ChallengeInstanceId == command.ChallengeInstanceId
        && existing.Result == command.Result
        && existing.FailureCode == command.FailureCode
        && existing.OccurredAt == command.OccurredAt
        && existing.EvaluatorVersion == command.EvaluatorVersion;
}

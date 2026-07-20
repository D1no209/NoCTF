using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.Infrastructure.BackgroundWork;
using DomainSubmissionKind = NoCTF.Domain.Submissions.SubmissionKind;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

/// <summary>Accepts Submission facts atomically with idempotency and attempt-limit checks.</summary>
public sealed class EfSubmissionIntakeStore(
    NoCtfDbContext db,
    IBackgroundWorkScheduler scheduler,
    IBackgroundWorkAdmissionGate admissionGate,
    TimeProvider? configuredTimeProvider = null)
    : ISubmissionIntakeStore
{
    public async Task<SubmissionAcceptanceResult?> FindAcceptedAsync(
        Guid competitionId,
        string idempotencyKey,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        DomainSubmissionKind kind,
        AwdAttackTarget? attackTarget,
        Guid? stageId,
        Guid? challengeInstanceId,
        FlagFingerprint? flagFingerprint,
        CancellationToken ct)
    {
        if (!admissionGate.IsAccepting)
            return new(SubmissionAcceptanceState.BackgroundWorkUnavailable);

        var existing = await db.Submissions.AsNoTracking().SingleOrDefaultAsync(
            submission => submission.CompetitionId == competitionId
                          && submission.IdempotencyKey == idempotencyKey,
            ct);
        if (existing is null)
            return null;
        if (!Matches(existing, teamId, challengeId, userId, kind, attackTarget, stageId, challengeInstanceId, flagFingerprint))
            return new(SubmissionAcceptanceState.IdempotencyConflict);

        if (existing.ScoringEventId is null)
        {
            using var admission = admissionGate.TryEnter();
            if (admission is null)
                return new(SubmissionAcceptanceState.BackgroundWorkUnavailable);
            using var enqueueCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, admission.DrainCancellation);
            try
            {
                await scheduler.EnqueueSubmissionAsync(existing.Id, enqueueCancellation.Token);
            }
            catch (BackgroundWorkUnavailableException)
            {
                return new(SubmissionAcceptanceState.BackgroundWorkUnavailable);
            }
            catch (OperationCanceledException) when (admission.DrainCancellation.IsCancellationRequested)
            {
                return new(SubmissionAcceptanceState.BackgroundWorkUnavailable);
            }
        }

        return new(SubmissionAcceptanceState.Existing, existing.Id, existing.ReceivedAt);
    }

    public async Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        CancellationToken ct)
        => await SubmissionAdmissionPersistence.LoadAsync(
            db, competitionId, teamId, challengeId, userId,
            (configuredTimeProvider ?? TimeProvider.System).GetUtcNow(), ct);

    public async Task<SubmissionAcceptanceResult> TryAcceptFlagAsync(
        FlagSubmissionReceived received,
        SubmissionAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken ct)
    {
        using var admission = admissionGate.TryEnter();
        if (admission is null)
            return new(SubmissionAcceptanceState.BackgroundWorkUnavailable);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, received.CompetitionId, ct);
        if (status != CompetitionStatus.Running)
            return new(SubmissionAcceptanceState.SnapshotChanged);

        var existing = await FindAcceptedAsync(
            received.CompetitionId, received.IdempotencyKey, received.TeamId, received.ChallengeId,
            received.UserId, DomainSubmissionKind.Flag, received.AttackTarget, received.StageId,
            received.ChallengeInstanceId, received.FlagFingerprint, ct);
        if (existing is not null)
            return existing;

        var current = await LoadAdmissionAsync(
            received.CompetitionId, received.TeamId, received.ChallengeId, received.UserId, ct);
        if (!SubmissionAdmissionPersistence.Matches(snapshot, current))
            return new(SubmissionAcceptanceState.SnapshotChanged);
        if (maxAttempts is > 0 && current!.AcceptedFlagAttempts >= maxAttempts)
            return new(SubmissionAcceptanceState.AttemptsExhausted);

        var entity = new Submission
        {
            Id = received.SubmissionId,
            CompetitionId = received.CompetitionId,
            TeamId = received.TeamId,
            ChallengeId = received.ChallengeId,
            UserId = received.UserId,
            Kind = DomainSubmissionKind.Flag,
            FlagHash = received.FlagFingerprint.Sha256,
            FlagLength = received.FlagFingerprint.Length,
            SubjectTeamId = received.AttackTarget?.TeamId,
            VictimTeamId = received.AttackTarget?.TeamId,
            ServiceId = received.AttackTarget?.ServiceId,
            StageId = received.StageId,
            ChallengeInstanceId = received.ChallengeInstanceId,
            IdempotencyKey = received.IdempotencyKey,
            ReceivedAt = received.ReceivedAt,
            CreatedAt = received.ReceivedAt,
            UpdatedAt = received.ReceivedAt
        };
        db.Submissions.Add(entity);
        var result = await SaveAsync(entity, transaction, received.IdempotencyKey, received.TeamId,
            received.ChallengeId, received.UserId, DomainSubmissionKind.Flag, ct);
        if (result.State == SubmissionAcceptanceState.Created)
        {
            using var enqueueCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, admission.DrainCancellation);
            try { await scheduler.EnqueueSubmissionAsync(entity.Id, enqueueCancellation.Token); }
            catch (BackgroundWorkUnavailableException) { return new(SubmissionAcceptanceState.BackgroundWorkUnavailable); }
            catch (OperationCanceledException) when (admission.DrainCancellation.IsCancellationRequested)
            {
                return new(SubmissionAcceptanceState.BackgroundWorkUnavailable);
            }
        }
        return result;
    }

    public async Task<SubmissionAcceptanceResult> TryAcceptFixAsync(
        FixSubmissionReceived received,
        SubmissionAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken ct)
    {
        using var admission = admissionGate.TryEnter();
        if (admission is null)
            return new(SubmissionAcceptanceState.BackgroundWorkUnavailable);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, received.CompetitionId, ct);
        if (status != CompetitionStatus.Running)
            return new(SubmissionAcceptanceState.SnapshotChanged);

        var existing = await FindAcceptedAsync(
            received.CompetitionId, received.IdempotencyKey, received.TeamId, received.ChallengeId,
            received.UserId, DomainSubmissionKind.Fix, null, null, null, null, ct);
        if (existing is not null)
            return existing;

        var current = await LoadAdmissionAsync(
            received.CompetitionId, received.TeamId, received.ChallengeId, received.UserId, ct);
        if (!SubmissionAdmissionPersistence.Matches(snapshot, current))
            return new(SubmissionAcceptanceState.SnapshotChanged);
        if (maxAttempts is > 0 && current!.AcceptedFixAttempts >= maxAttempts)
            return new(SubmissionAcceptanceState.AttemptsExhausted);

        var record = await db.FixSubmissionRecords.SingleOrDefaultAsync(
            item => item.UploadId == received.UploadId
                    && item.VerificationStatus == FixVerificationStatus.Created,
            ct);
        if (record is null || record.SubmissionId is not null || record.ExpiresAt <= received.ReceivedAt
            || record.CompetitionId != received.CompetitionId || record.TeamId != received.TeamId
            || record.ChallengeId != received.ChallengeId)
            return new(SubmissionAcceptanceState.UploadUnavailable);

        var entity = new Submission
        {
            Id = received.SubmissionId,
            CompetitionId = received.CompetitionId,
            TeamId = received.TeamId,
            ChallengeId = received.ChallengeId,
            UserId = received.UserId,
            Kind = DomainSubmissionKind.Fix,
            IdempotencyKey = received.IdempotencyKey,
            ReceivedAt = received.ReceivedAt,
            CreatedAt = received.ReceivedAt,
            UpdatedAt = received.ReceivedAt
        };
        record.SubmissionId = entity.Id;
        record.ClaimedAt = received.ReceivedAt;
        record.VerificationStatus = FixVerificationStatus.Claimed;
        record.UpdatedAt = received.ReceivedAt;
        db.Submissions.Add(entity);

        var result = await SaveAsync(entity, transaction, received.IdempotencyKey, received.TeamId,
            received.ChallengeId, received.UserId, DomainSubmissionKind.Fix, ct);
        if (result.State == SubmissionAcceptanceState.Created)
        {
            using var enqueueCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, admission.DrainCancellation);
            try { await scheduler.EnqueueSubmissionAsync(entity.Id, enqueueCancellation.Token); }
            catch (BackgroundWorkUnavailableException) { return new(SubmissionAcceptanceState.BackgroundWorkUnavailable); }
            catch (OperationCanceledException) when (admission.DrainCancellation.IsCancellationRequested)
            {
                return new(SubmissionAcceptanceState.BackgroundWorkUnavailable);
            }
        }
        return result;
    }

    private async Task<SubmissionAcceptanceResult> SaveAsync(
        Submission entity,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string idempotencyKey,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        DomainSubmissionKind kind,
        CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(SubmissionAcceptanceState.Created, entity.Id, entity.ReceivedAt);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var attackTarget = entity.SubjectTeamId is { } target && entity.ServiceId is { } service
                ? new AwdAttackTarget(target, service)
                : null;
            return await FindAcceptedAsync(
                    entity.CompetitionId, idempotencyKey, teamId, challengeId, userId, kind, attackTarget,
                    entity.StageId,
                    entity.ChallengeInstanceId,
                    entity.FlagHash is not null && entity.FlagLength is { } length
                        ? new FlagFingerprint(entity.FlagHash, length)
                        : null,
                    ct)
                ?? new SubmissionAcceptanceResult(SubmissionAcceptanceState.SnapshotChanged);
        }
    }

    private static bool Matches(
        Submission submission,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        DomainSubmissionKind kind,
        AwdAttackTarget? attackTarget,
        Guid? stageId,
        Guid? challengeInstanceId,
        FlagFingerprint? flagFingerprint) =>
        submission.TeamId == teamId
        && submission.ChallengeId == challengeId
        && submission.UserId == userId
        && submission.Kind == kind
        && submission.SubjectTeamId == attackTarget?.TeamId
        && submission.VictimTeamId == attackTarget?.TeamId
        && submission.ServiceId == attackTarget?.ServiceId
        && submission.StageId == stageId
        && submission.ChallengeInstanceId == challengeInstanceId
        && (flagFingerprint is null
            ? submission.FlagHash is null && submission.LegacyFlag is null
            : submission.FlagHash == flagFingerprint.Value.Sha256
              && submission.FlagLength == flagFingerprint.Value.Length
              || submission.LegacyFlag is { } legacyFlag
              && flagFingerprint.Value.Matches(legacyFlag));

}

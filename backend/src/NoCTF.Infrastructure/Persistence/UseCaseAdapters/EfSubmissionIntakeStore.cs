using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfSubmissionIntakeStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox) : ISubmissionIntakeStore
{
    public Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken) =>
        SubmissionAdmissionPersistence.LoadAsync(
            db, competitionId, competitionChallengeId, userId, cancellationToken);

    public async Task<SubmissionAcceptanceResult> TryAcceptFlagAsync(
        FlagSubmissionReceived received,
        SubmissionAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken)
    {
        var results = await TryAcceptFlagsAsync(
            [received], snapshot, maxAttempts, cancellationToken);
        return results[0];
    }

    public async Task<IReadOnlyList<SubmissionAcceptanceResult>> TryAcceptFlagsAsync(
        IReadOnlyList<FlagSubmissionReceived> received,
        SubmissionAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken)
    {
        if (received.Count == 0)
            return [];
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        await AcquireAttemptLockAsync(
            received[0].TeamId,
            received[0].CompetitionChallengeId,
            received[0].Kind,
            cancellationToken);
        var current = await LoadAdmissionAsync(
            received[0].CompetitionId,
            received[0].CompetitionChallengeId,
            received[0].UserId,
            cancellationToken);
        if (!SubmissionAdmissionPersistence.Matches(snapshot, current)
            || current!.CompetitionStatus != CompetitionStatus.Running)
            return received.Select(_ => new SubmissionAcceptanceResult(
                SubmissionAcceptanceState.SnapshotChanged)).ToArray();
        if (maxAttempts is > 0
            && checked(current.AcceptedFlagAttempts + received.Count) > maxAttempts)
            return received.Select(_ => new SubmissionAcceptanceResult(
                SubmissionAcceptanceState.AttemptsExhausted)).ToArray();

        var entities = received.Select(item => new Submission
        {
            Id = item.SubmissionId,
            CompetitionId = item.CompetitionId,
            CompetitionChallengeId = item.CompetitionChallengeId,
            TeamId = item.TeamId,
            SubmittedByUserId = item.UserId,
            Kind = item.Kind,
            SubmittedFlag = item.SubmittedFlag,
            SubmittedFlagSha256 = item.SubmittedFlagSha256,
            ReceivedAt = item.ReceivedAt,
            EvaluationState = SubmissionEvaluationState.Queued,
            EvaluationUpdatedAt = item.ReceivedAt,
            ProcessingVersion = 0
        }).ToArray();
        db.Submissions.AddRange(entities);
        foreach (var entity in entities)
            await outbox.PublishAsync(new EvaluateSubmission(entity.Id, entity.ProcessingVersion));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return entities.Select(entity => new SubmissionAcceptanceResult(
            SubmissionAcceptanceState.Created,
            entity.Id,
            entity.ReceivedAt)).ToArray();
    }

    public async Task<SubmissionAcceptanceResult> TryAcceptFixAsync(
        FixSubmissionReceived received,
        SubmissionAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        await AcquireAttemptLockAsync(
            received.TeamId, received.CompetitionChallengeId, SubmissionKind.Fix, cancellationToken);
        var current = await LoadAdmissionAsync(
            received.CompetitionId, received.CompetitionChallengeId, received.UserId, cancellationToken);
        if (!SubmissionAdmissionPersistence.Matches(snapshot, current)
            || current!.CompetitionStatus != CompetitionStatus.Running)
            return new(SubmissionAcceptanceState.SnapshotChanged);
        if (maxAttempts is > 0 && current.AcceptedFixAttempts >= maxAttempts)
            return new(SubmissionAcceptanceState.AttemptsExhausted);

        var patch = await db.PatchUploads.SingleOrDefaultAsync(
            upload => upload.Id == received.PatchUploadId
                && upload.CompetitionId == received.CompetitionId
                && upload.CompetitionChallengeId == received.CompetitionChallengeId
                && upload.TeamId == received.TeamId
                && upload.ConsumedAt == null
                && upload.SubmissionId == null,
            cancellationToken);
        if (patch is null)
            return new(SubmissionAcceptanceState.PatchUploadUnavailable);

        var entity = new Submission
        {
            Id = received.SubmissionId,
            CompetitionId = received.CompetitionId,
            CompetitionChallengeId = received.CompetitionChallengeId,
            TeamId = received.TeamId,
            SubmittedByUserId = received.UserId,
            Kind = SubmissionKind.Fix,
            PatchUploadId = patch.Id,
            ReceivedAt = received.ReceivedAt,
            EvaluationState = SubmissionEvaluationState.Queued,
            EvaluationUpdatedAt = received.ReceivedAt,
            ProcessingVersion = 0
        };
        patch.ConsumedAt = received.ReceivedAt;
        patch.SubmissionId = entity.Id;
        db.Submissions.Add(entity);
        await outbox.PublishAsync(new EvaluateSubmission(entity.Id, entity.ProcessingVersion));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return new(SubmissionAcceptanceState.Created, entity.Id, entity.ReceivedAt);
    }

    private Task AcquireAttemptLockAsync(
        Guid teamId,
        Guid competitionChallengeId,
        SubmissionKind kind,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({'s' + teamId.ToString("N") + competitionChallengeId.ToString("N") + ((short)kind).ToString()}, 0))",
            cancellationToken);
}

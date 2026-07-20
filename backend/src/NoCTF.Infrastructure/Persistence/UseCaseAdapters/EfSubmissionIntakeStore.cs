using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Domain.Submissions;
using NoCTF.Infrastructure.BackgroundWork;
using DomainSubmissionKind = NoCTF.Domain.Submissions.SubmissionKind;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

/// <summary>Accepts Submission facts atomically with idempotency and attempt-limit checks.</summary>
public sealed class EfSubmissionIntakeStore(
    NoCtfDbContext db,
    IBackgroundWorkScheduler scheduler,
    IBackgroundWorkAdmissionGate admissionGate)
    : ISubmissionIntakeStore
{
    public async Task<SubmissionAcceptanceResult?> FindAcceptedAsync(
        Guid competitionId,
        string idempotencyKey,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        DomainSubmissionKind kind,
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
        if (!Matches(existing, teamId, challengeId, userId, kind))
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
    {
        var scope = await (
            from competition in db.Competitions.AsNoTracking()
            join competitionConfiguration in db.CompetitionConfigurations.AsNoTracking()
                on competition.Id equals competitionConfiguration.CompetitionId
            join challenge in db.Challenges.AsNoTracking()
                on competition.Id equals challenge.CompetitionId
            join challengeConfiguration in db.ChallengeConfigurations.AsNoTracking()
                on challenge.Id equals challengeConfiguration.ChallengeId
            join team in db.Teams.AsNoTracking()
                on competition.Id equals team.CompetitionId
            where competition.Id == competitionId && challenge.Id == challengeId && team.Id == teamId
            select new
            {
                Competition = competition,
                CompetitionConfiguration = competitionConfiguration,
                Challenge = challenge,
                ChallengeConfiguration = challengeConfiguration,
                Team = team
            }).SingleOrDefaultAsync(ct);
        if (scope is null)
            return null;

        var belongs = await db.TeamMembers.AsNoTracking().AnyAsync(
            member => member.CompetitionId == competitionId
                      && member.TeamId == teamId
                      && member.UserId == userId,
            ct);
        var attempts = await db.Submissions.AsNoTracking()
            .Where(submission => submission.CompetitionId == competitionId
                                 && submission.TeamId == teamId
                                 && submission.ChallengeId == challengeId)
            .GroupBy(submission => submission.Kind)
            .Select(group => new { Kind = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Kind, item => item.Count, ct);

        return new(
            competitionId,
            teamId,
            challengeId,
            scope.Competition.Mode,
            scope.CompetitionConfiguration.Revision,
            scope.ChallengeConfiguration.Revision,
            scope.CompetitionConfiguration.Json,
            scope.ChallengeConfiguration.Json,
            attempts.GetValueOrDefault(DomainSubmissionKind.Flag),
            attempts.GetValueOrDefault(DomainSubmissionKind.Fix),
            scope.Competition.Status,
            scope.Competition.StartTime,
            scope.Competition.EndTime,
            scope.Competition.Deletion.IsDeleted,
            scope.Challenge.Deletion.IsDeleted,
            scope.Challenge.IsPublished,
            scope.Team.Deletion.IsDeleted,
            scope.Team.Ban.IsBanned,
            scope.Team.RegistrationStatus == NoCTF.Domain.Teams.TeamRegistrationStatus.Approved,
            belongs);
    }

    public async Task<SubmissionAcceptanceResult> TryAcceptFlagAsync(
        FlagSubmissionReceived received,
        SubmissionAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken ct)
    {
        using var admission = admissionGate.TryEnter();
        if (admission is null)
            return new(SubmissionAcceptanceState.BackgroundWorkUnavailable);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await FindAcceptedAsync(
            received.CompetitionId, received.IdempotencyKey, received.TeamId, received.ChallengeId,
            received.UserId, DomainSubmissionKind.Flag, ct);
        if (existing is not null)
            return existing;

        var current = await LoadAdmissionAsync(
            received.CompetitionId, received.TeamId, received.ChallengeId, received.UserId, ct);
        if (!MatchesSnapshot(snapshot, current))
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
            Flag = received.Flag,
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

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await FindAcceptedAsync(
            received.CompetitionId, received.IdempotencyKey, received.TeamId, received.ChallengeId,
            received.UserId, DomainSubmissionKind.Fix, ct);
        if (existing is not null)
            return existing;

        var current = await LoadAdmissionAsync(
            received.CompetitionId, received.TeamId, received.ChallengeId, received.UserId, ct);
        if (!MatchesSnapshot(snapshot, current))
            return new(SubmissionAcceptanceState.SnapshotChanged);
        if (maxAttempts is > 0 && current!.AcceptedFixAttempts >= maxAttempts)
            return new(SubmissionAcceptanceState.AttemptsExhausted);

        var record = await db.FixSubmissionRecords.SingleOrDefaultAsync(
            item => item.UploadId == received.UploadId,
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
            return await FindAcceptedAsync(entity.CompetitionId, idempotencyKey, teamId, challengeId, userId, kind, ct)
                ?? new SubmissionAcceptanceResult(SubmissionAcceptanceState.SnapshotChanged);
        }
    }

    private static bool Matches(
        Submission submission,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        DomainSubmissionKind kind) =>
        submission.TeamId == teamId
        && submission.ChallengeId == challengeId
        && submission.UserId == userId
        && submission.Kind == kind;

    private static bool MatchesSnapshot(
        SubmissionAdmissionSnapshot expected,
        SubmissionAdmissionSnapshot? current) =>
        current is not null
        && current.Mode == expected.Mode
        && current.CompetitionConfigurationRevision == expected.CompetitionConfigurationRevision
        && current.ChallengeConfigurationRevision == expected.ChallengeConfigurationRevision
        && current.CompetitionStatus == expected.CompetitionStatus
        && current.CompetitionDeleted == expected.CompetitionDeleted
        && current.ChallengeDeleted == expected.ChallengeDeleted
        && current.ChallengePublished == expected.ChallengePublished
        && current.TeamDeleted == expected.TeamDeleted
        && current.TeamBanned == expected.TeamBanned
        && current.TeamApproved == expected.TeamApproved
        && current.UserBelongsToTeam == expected.UserBelongsToTeam;
}

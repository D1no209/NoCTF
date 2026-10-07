using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges.WriteUps;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Shared;

namespace NoCTF.Infrastructure.Challenges.WriteUps;

public sealed partial class ChallengeWriteUpStore
{
    public async Task<WriteUpContentView> ReadUnlockCandidateAsync(UnlockWriteUp command, CancellationToken ct)
    {
        var context = await ContextAsync(command.CompetitionId, command.CompetitionChallengeId, command.ActorId, false, command.Now, ct);
        if (context is null) return new(command.VersionId, default, null, null, ChallengeWriteUpFailure.Forbidden);
        var root = await db.ChallengeWriteUps.AsNoTracking().Include(x => x.Versions)
            .SingleOrDefaultAsync(x => x.CompetitionId == command.CompetitionId && x.CompetitionChallengeId == command.CompetitionChallengeId
                && x.PublishedVersionId == command.VersionId, ct);
        if (root is null) return new(command.VersionId, default, null, null, ChallengeWriteUpFailure.NotPublished);
        var version = root.Versions.Single(x => x.Id == command.VersionId);
        var file = version.FileId is Guid id ? await db.Files.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        return new(version.Id, version.Format, version.Markdown,
            file is null ? null : new(version.Id, file.ObjectKey, file.FileName, file.ContentType));
    }

    public async Task<WriteUpUnlockResult> UnlockAsync(UnlockWriteUp command, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var context = await ContextAsync(command.CompetitionId, command.CompetitionChallengeId, command.ActorId, false, command.Now, ct);
                if (context?.TeamId is not Guid teamId) return new(false, 0, ChallengeWriteUpFailure.Forbidden);
                var root = await db.ChallengeWriteUps.AsNoTracking().SingleOrDefaultAsync(x => x.CompetitionId == command.CompetitionId
                    && x.CompetitionChallengeId == command.CompetitionChallengeId && x.PublishedVersionId == command.VersionId, ct);
                if (root is null) return new(false, 0, ChallengeWriteUpFailure.ConfirmationChanged);
                var existing = await db.WriteUpUnlockReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.TeamId == teamId
                    && x.CompetitionChallengeId == command.CompetitionChallengeId, ct);
                if (existing is not null) return new(false, existing.DeductionPercent);
                if (root.TeamId == teamId || context.Competition.Status == CompetitionStatus.Finished) return new(false, 0);
                var access = Access(context, null, command.Now);
                if (!access.CanUnlock) return new(false, 0, ChallengeWriteUpFailure.CompetitionNotRunning);
                if (access.Settings.PolicyStamp != command.PolicyStamp) return new(false, 0, ChallengeWriteUpFailure.ConfirmationChanged);
                var factId = Guid.CreateVersion7(command.Now);
                db.GameplayFacts.Add(new WriteUpUnlockGameplayFact { Id = factId, CompetitionId = command.CompetitionId,
                    CompetitionChallengeId = command.CompetitionChallengeId, TeamId = teamId, ActorUserId = command.ActorId,
                    OccurredAt = command.Now, UpdatedAt = command.Now, ReferenceKind = GameplayFactReferenceKind.WriteUpVersion,
                    ReferenceId = command.VersionId, State = GameplayFactState.Queued, Result = GameplayFactResult.Unlocked });
                db.WriteUpUnlockReceipts.Add(new() { GameplayFactId = factId, CompetitionId = command.CompetitionId,
                    CompetitionChallengeId = command.CompetitionChallengeId, TeamId = teamId, VersionId = command.VersionId,
                    DeductionPercent = access.DeductionPercent, UnlockedAt = command.Now });
                await events.RecordAsync(new(command.CompetitionId, CompetitionEventKind.ChallengeWriteUpUnlocked,
                    CompetitionEventLevel.Information, CompetitionEventVisibility.Team, command.Now, ActorUserId: command.ActorId,
                    TeamId: teamId, CompetitionChallengeId: command.CompetitionChallengeId, GameplayFactId: factId,
                    SubjectType: EntityReferenceKind.CompetitionChallenge, SubjectId: command.CompetitionChallengeId), ct);
                await messages.PublishAsync(new ProjectLeaderboard(command.CompetitionId));
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); committed = true;
                if (leaderboards is not null) await leaderboards.InvalidateAsync(command.CompetitionId, ct);
                await messages.FlushCommittedMessagesAsync();
                return new(true, access.DeductionPercent);
            }
            catch (Exception exception) when (!committed && attempt < 2 && (exception is DbUpdateException
                || NoCTF.Infrastructure.Persistence.TransactionFailureClassifier.IsRetryable(exception)))
            {
                db.ChangeTracker.Clear(); messages.DiscardPendingMessages();
            }
            catch (Exception exception) when (!committed && (exception is DbUpdateException
                || NoCTF.Infrastructure.Persistence.TransactionFailureClassifier.IsRetryable(exception)))
            {
                db.ChangeTracker.Clear(); messages.DiscardPendingMessages();
                return new(false, 0, ChallengeWriteUpFailure.Conflict);
            }
        }
    }

    public async Task<WriteUpSettingsResult> GetSettingsAsync(Guid competitionId, Guid? challengeId, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == competitionId, ct);
        if (competition is null || await readAccess.ResolveAsync(actorId, competitionId, now, ct) is null)
            return new(null, null, ChallengeWriteUpFailure.Forbidden);
        var challenge = challengeId is Guid id ? await db.CompetitionChallenges.AsNoTracking().IgnoreAutoIncludes()
            .SingleOrDefaultAsync(x => x.Id == id && x.CompetitionId == competitionId, ct) : null;
        if (challengeId is not null && challenge is null) return new(null, null, ChallengeWriteUpFailure.NotFound);
        return new(new(competition.SingleWriteUpsEnabled, challenge?.WriteUpDeductionPercent ?? competition.SingleWriteUpDeductionPercent,
            competition.SingleWriteUpDeadlineHours, CompetitionWriteUpPolicy.DeadlineAt(competition.EndAt, competition.SingleWriteUpDeadlineHours),
            challenge?.WriteUpDeductionPercent, challenge?.ConcurrencyStamp ?? competition.ConcurrencyStamp),
            challenge?.ConcurrencyStamp ?? competition.ConcurrencyStamp);
    }

    public async Task<WriteUpSettingsResult> UpdateSettingsAsync(UpdateWriteUpSettings command, CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct)) return new(null, null, ChallengeWriteUpFailure.Forbidden);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var competition = await db.Competitions.SingleOrDefaultAsync(x => x.Id == command.CompetitionId, ct);
        if (competition is null) return new(null, null, ChallengeWriteUpFailure.NotFound);
        if (command.ChallengeId is Guid challengeId)
        {
            var challenge = await db.CompetitionChallenges.SingleOrDefaultAsync(x => x.Id == challengeId && x.CompetitionId == command.CompetitionId, ct);
            if (challenge is null) return new(null, null, ChallengeWriteUpFailure.NotFound);
            if (challenge.ConcurrencyStamp != command.ExpectedStamp) return new(null, null, ChallengeWriteUpFailure.Conflict);
            if (command.DeductionSpecified) challenge.WriteUpDeductionPercent = command.DeductionPercent;
            challenge.UpdatedAt = command.Now;
        }
        else
        {
            if (competition.ConcurrencyStamp != command.ExpectedStamp) return new(null, null, ChallengeWriteUpFailure.Conflict);
            if (command.Enabled is bool enabled) competition.SingleWriteUpsEnabled = enabled;
            if (command.DeductionPercent is int percent) competition.SingleWriteUpDeductionPercent = percent;
            if (command.DeadlineHours is int hours) competition.SingleWriteUpDeadlineHours = hours;
            competition.UpdatedAt = command.Now;
        }
        await events.RecordAsync(new(command.CompetitionId, CompetitionEventKind.CompetitionUpdated,
            CompetitionEventLevel.Information, CompetitionEventVisibility.Staff, command.Now, ActorUserId: command.ActorId,
            SubjectType: EntityReferenceKind.Competition, SubjectId: command.CompetitionId), ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await messages.FlushCommittedMessagesAsync();
        return await GetSettingsAsync(command.CompetitionId, command.ChallengeId, command.ActorId, command.Now, ct);
    }
}

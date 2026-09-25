using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.GameModes.PatchVerification.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.Domain.Challenges;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.Observability;

namespace NoCTF.Infrastructure.GameplayFacts.PatchUploads;

public sealed class PatchUploadStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    GameplayFactAttemptCriticalSection attemptCriticalSection,
    FileReferenceLock fileLock,
    ILogger<PatchUploadStore> logger,
    ICompetitionEventRecorder? eventRecorder = null,
    NoCTF.Application.Authentication.Privacy.IRequestSourceAddress? source = null,
    IRequestReplay? replay = null) : IPatchUploadStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public Task<bool> CanAccessTargetAsync(Guid competitionId, Guid challengeId, Guid targetId, Guid userId, CancellationToken ct) =>
        db.RuntimeInstances.AsNoTracking().Where(target => target.Id == targetId && target.CompetitionId == competitionId
                && target.CompetitionChallengeId == challengeId
                && (target.Purpose == RuntimePurpose.AwdpTarget
                    || target.Purpose == RuntimePurpose.PatchVerificationTarget))
            .Join(db.Teams.Where(team => team.DeletedAt == null && !team.IsBanned && team.RegistrationStatus == TeamRegistrationStatus.Approved
                    && team.CompetitionId == competitionId && team.Members.Any(member => member.UserId == userId)), target => target.TeamId, team => (Guid?)team.Id,
                (target, team) => target.Id).AnyAsync(ct);

    public Task<bool> IsTargetUnconsumedAsync(PatchUploadScope scope, CancellationToken ct) =>
        db.RuntimeInstances.AsNoTracking().AnyAsync(target => target.Id == scope.RuntimeInstanceId
            && target.CompetitionId == scope.CompetitionId && target.CompetitionChallengeId == scope.CompetitionChallengeId
            && target.TeamId == scope.TeamId
            && (target.Purpose == RuntimePurpose.AwdpTarget
                || target.Purpose == RuntimePurpose.PatchVerificationTarget)
            && target.GameplayFactId == null
            && (target.State == RuntimeState.Queued || target.State == RuntimeState.Provisioning || target.State == RuntimeState.Running), ct);

    public PatchUploadStore(
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        ILogger<PatchUploadStore> logger)
        : this(
            db,
            outbox,
            new GameplayFactAttemptCriticalSection(),
            new FileReferenceLock(),
            logger,
            null)
    { }

    public async Task<PatchUploadScope?> ResolveScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        Guid userId,
        CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId
                && item.DeletedAt == null
                && !item.IsBanned
                && item.RegistrationStatus == TeamRegistrationStatus.Approved
                && item.Members.Any(member => member.UserId == userId))
            .Select(item => new { item.Id })
            .SingleOrDefaultAsync(ct);
        if (team is null)
            return null;
        var available = await db.CompetitionChallenges.AsNoTracking()
            .Join(
                db.Competitions.AsNoTracking(),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { challenge, competition })
            .Join(
                db.Challenges.AsNoTracking(),
                item => item.challenge.ChallengeId,
                challenge => challenge.Id,
                (item, challenge) => new { item.challenge, item.competition, template = challenge })
            .Where(item => item.challenge.Id == competitionChallengeId
                && item.challenge.CompetitionId == competitionId
                && item.challenge.DeletedAt == null
                && (item.competition.Mode == GameMode.Awdp
                    && item.competition.Status == CompetitionStatus.Running
                    || item.competition.Mode == GameMode.Ctf
                    && (item.competition.Status == CompetitionStatus.Running
                        || item.competition.Status == CompetitionStatus.Paused)))
            .SingleOrDefaultAsync(ct);
        if (available is null)
            return null;
        var defenseAlreadySucceeded = await db.GameplayFacts.AsNoTracking().AnyAsync(fact =>
            fact.CompetitionId == competitionId
            && fact.CompetitionChallengeId == competitionChallengeId
            && fact.TeamId == team.Id
            && fact.Kind == GameplayFactKind.FixAttempt
            && fact.Result == GameplayFactResult.Correct,
            ct);
        if (defenseAlreadySucceeded)
            return null;
        if (available.competition.Mode == GameMode.Ctf
            && available.template.Definition is not CtfChallengeDefinition
                { InteractionKind: CtfInteractionKind.PatchVerification })
        {
            return null;
        }
        var configuration = PatchVerificationConfigurationResolver.Resolve(
            available.competition.Mode,
            available.competition.ModeConfiguration!,
            available.challenge.Rules!,
            available.template.Definition!);
        if (configuration is null)
            return null;
        var expectedPurpose = available.competition.Mode == GameMode.Ctf
            ? RuntimePurpose.PatchVerificationTarget
            : RuntimePurpose.AwdpTarget;
        var targetExists = await db.RuntimeInstances.AsNoTracking().AnyAsync(instance =>
            instance.Id == runtimeInstanceId
            && instance.CompetitionId == competitionId
            && instance.CompetitionChallengeId == competitionChallengeId
            && instance.TeamId == team.Id
            && instance.Purpose == expectedPurpose,
            ct);
        return configuration.MaximumPatchUploadBytes > 0 && targetExists
            ? new(
                competitionId,
                competitionChallengeId,
                team.Id,
                userId,
                runtimeInstanceId,
                configuration.MaximumPatchUploadBytes,
                expectedPurpose)
            : null;
    }

    public async Task<PatchUploadSaveResult> SaveAsync(
        Guid patchUploadId,
        Guid gameplayFactId,
        PatchUploadScope scope,
        Guid fileId,
        DateTimeOffset uploadedAt,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        using var attemptLease = await attemptCriticalSection.AcquireAsync(
            db,
            scope.TeamId,
            scope.CompetitionChallengeId,
            GameplayFactKind.FixAttempt,
            ct);
        if (!await fileLock.AcquireAsync(db, fileId, ct))
            return new(PatchUploadSaveState.ConcurrencyConflict);

        var target = await db.RuntimeInstances.SingleOrDefaultAsync(
            instance => instance.Id == scope.RuntimeInstanceId,
            ct);
        if (target is null
            || target.Purpose is not (RuntimePurpose.AwdpTarget
                or RuntimePurpose.PatchVerificationTarget)
            || target.CompetitionId != scope.CompetitionId
            || target.CompetitionChallengeId != scope.CompetitionChallengeId
            || target.TeamId != scope.TeamId)
        {
            return new(PatchUploadSaveState.DefenseTargetNotReady);
        }
        if (target.GameplayFactId is not null
            || await db.PatchUploads.AnyAsync(
                upload => upload.RuntimeInstanceId == target.Id,
                ct))
        {
            return new(PatchUploadSaveState.DefenseTargetConsumed);
        }
        if (target.State is not (RuntimeState.Queued
                or RuntimeState.Provisioning
                or RuntimeState.Running))
        {
            return new(PatchUploadSaveState.DefenseTargetNotReady);
        }
        var targetIsRunning = target.State == RuntimeState.Running;
        if (targetIsRunning
            && (target.RunnerId is null
                || target.ExpiresAt is not { } expiresAt
                || expiresAt <= uploadedAt))
        {
            return new(PatchUploadSaveState.DefenseTargetNotReady);
        }

        var admission = await GameplayFactAdmissionPersistence.LoadAsync(
            db,
            scope.CompetitionId,
            scope.CompetitionChallengeId,
            scope.UserId,
            ct);
        if (admission is null
            || admission.Mode == GameMode.Awdp
                && admission.CompetitionStatus != CompetitionStatus.Running
            || admission.Mode == GameMode.Ctf
                && admission.CompetitionStatus is not (CompetitionStatus.Running
                    or CompetitionStatus.Paused))
            return new(PatchUploadSaveState.DefenseTargetNotReady);
        if (admission.HasCorrectFix)
            return new(PatchUploadSaveState.AchievementAlreadySucceeded);
        var context = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == scope.CompetitionChallengeId)
            .Join(
                db.Challenges.AsNoTracking(),
                challenge => challenge.ChallengeId,
                template => template.Id,
                (challenge, template) => new { Challenge = challenge, Template = template })
            .SingleAsync(ct);
        var configuration = PatchVerificationConfigurationResolver.Resolve(
            admission.Mode,
            admission.CompetitionConfiguration,
            context.Challenge.Rules!,
            context.Template.Definition!);
        if (configuration is null)
            return new(PatchUploadSaveState.DefenseTargetNotReady);
        if (configuration.MaximumAttempts > 0
            && admission.AcceptedFixAttempts >= configuration.MaximumAttempts)
        {
            return new(PatchUploadSaveState.AttemptsExhausted);
        }
        if (configuration.RequiresBreak && !admission.HasCorrectBreak)
            return new(PatchUploadSaveState.DefenseTargetNotReady);
        db.PatchUploads.Add(new PatchUpload
        {
            Id = patchUploadId,
            CompetitionId = scope.CompetitionId,
            CompetitionChallengeId = scope.CompetitionChallengeId,
            TeamId = scope.TeamId,
            UploadedByUserId = scope.UserId,
            RuntimeInstanceId = target.Id,
            FileId = fileId,
            UploadedAt = uploadedAt
        });
        var fact = new FixAttemptGameplayFact
        {
            Id = gameplayFactId,
            CompetitionId = scope.CompetitionId,
            CompetitionChallengeId = scope.CompetitionChallengeId,
            TeamId = scope.TeamId,
            ActorUserId = scope.UserId,
            SourceIpAddress = source?.Address,
            ReferenceKind = GameplayFactReferenceKind.PatchUpload,
            ReferenceId = patchUploadId,
            OccurredAt = uploadedAt,
            State = GameplayFactState.Pending,
            UpdatedAt = uploadedAt
        };
        db.GameplayFacts.Add(fact);
        target.GameplayFactId = fact.Id;
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
        if (target.Purpose == RuntimePurpose.AwdpTarget)
            await outbox.PublishAsync(new StartAwdpFixVerification(fact.Id, target.Id));
        else
            await outbox.PublishAsync(new StartPatchVerification(fact.Id, target.Id));
        await events.RecordAsync(new(
            fact.CompetitionId,
            CompetitionEventKind.GameplayFactReceived,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            fact.OccurredAt,
            ActorUserId: fact.ActorUserId,
            TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId,
            RuntimeInstanceId: target.Id,
            GameplayFactId: fact.Id,
            GameplayFactKind: fact.Kind,
            GameplayFactState: fact.State,
            RuntimeState: target.State), ct);
        if (target.Purpose == RuntimePurpose.AwdpTarget)
        {
            await events.RecordAsync(new(
                fact.CompetitionId,
                CompetitionEventKind.AwdpFixAttempted,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public,
                fact.OccurredAt,
                TeamId: fact.TeamId,
                CompetitionChallengeId: fact.CompetitionChallengeId,
                GameplayFactId: fact.Id,
                GameplayFactKind: fact.Kind), ct);
        }
        try
        {
            replay?.Store(new AcceptedAwdpFix(patchUploadId, fact.Id, fact.State));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            NoCtfTelemetry.RecordGameplayFactSubmissions(GameplayFactKind.FixAttempt);
            try
            {
                await outbox.FlushCommittedMessagesAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Patch upload {PatchUploadId} committed, but its durable cleanup message was not flushed immediately.",
                    patchUploadId);
            }
            return new(PatchUploadSaveState.Accepted, fact.Id, fact.State);
        }
        catch (DbUpdateException exception) when (!TransactionFailureClassifier.IsRetryable(exception))
        {
            await transaction.RollbackAsync(ct);
            outbox.DiscardPendingMessages();
            db.ChangeTracker.Clear();
            if (await db.RuntimeInstances.AsNoTracking().AnyAsync(instance =>
                    instance.Id == scope.RuntimeInstanceId
                    && instance.GameplayFactId != null, ct)
                || await db.PatchUploads.AsNoTracking().AnyAsync(upload =>
                    upload.RuntimeInstanceId == scope.RuntimeInstanceId, ct))
                return new(PatchUploadSaveState.DefenseTargetConsumed);
            return new(PatchUploadSaveState.ConcurrencyConflict);
        }
    }
}

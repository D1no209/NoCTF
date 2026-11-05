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
using NoCTF.GameModes.Challenges.Capabilities;
using NoCTF.Domain.Challenges;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.Observability;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Application.Competitions.Awd.Fixes;
using Microsoft.EntityFrameworkCore.Storage;

namespace NoCTF.Infrastructure.GameplayFacts.PatchUploads;

public sealed class PatchUploadStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
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
                && (target.Purpose == RuntimePurpose.FixTarget
                    || target.Purpose == RuntimePurpose.ScopedFixTarget
                    || target.Purpose == RuntimePurpose.Player && db.Set<AwdCompetitionChallengeRules>().Any(rule =>
                        rule.CompetitionChallengeId == challengeId && rule.AllowFixSubmissions)))
            .Join(db.Teams.Where(team => team.DeletedAt == null && !team.IsBanned && team.RegistrationStatus == TeamRegistrationStatus.Approved
                    && team.CompetitionId == competitionId && team.Members.Any(member => member.UserId == userId)), target => target.TeamId, team => (Guid?)team.Id,
                (target, team) => target.Id).AnyAsync(ct);

    public Task<bool> IsTargetUnconsumedAsync(PatchUploadScope scope, CancellationToken ct) => scope.TargetKind == FixTargetKind.Persistent
        ? db.RuntimeInstances.AsNoTracking().AnyAsync(target => target.Id == scope.RuntimeInstanceId && target.CompetitionId == scope.CompetitionId
            && target.CompetitionChallengeId == scope.CompetitionChallengeId && target.TeamId == scope.TeamId
            && target.Purpose == RuntimePurpose.Player && target.State == RuntimeState.Running
            && !db.GameplayFacts.Any(fact => fact.FixExecution != null && fact.FixExecution.RuntimeInstanceId == target.Id
                && (fact.State == GameplayFactState.Pending || fact.State == GameplayFactState.Processing || fact.FixExecution.MutationBlocked)), ct)
        : db.RuntimeInstances.AsNoTracking().AnyAsync(target => target.Id == scope.RuntimeInstanceId
            && target.CompetitionId == scope.CompetitionId && target.CompetitionChallengeId == scope.CompetitionChallengeId
            && target.TeamId == scope.TeamId
            && (target.Purpose == RuntimePurpose.FixTarget
                || target.Purpose == RuntimePurpose.ScopedFixTarget)
            && target.GameplayFactId == null
            && (target.State == RuntimeState.Queued || target.State == RuntimeState.Provisioning || target.State == RuntimeState.Running), ct);

    public PatchUploadStore(
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        ILogger<PatchUploadStore> logger)
        : this(
            db,
            outbox,
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
                && (item.competition.Mode == GameMode.Awdp || item.competition.Mode == GameMode.Awd)
                && item.competition.Status == CompetitionStatus.Running)
            .AsSplitQuery()
            .SingleOrDefaultAsync(ct);
        if (available is null)
            return null;
        if (available.competition.Mode == GameMode.Awd)
        {
            if (available.challenge.Rules is not AwdCompetitionChallengeRules { AllowFixSubmissions: true }
                || available.template.Definition?.Fix is not { } fix) return null;
            var persistent = await db.RuntimeInstances.AsNoTracking().AnyAsync(instance => instance.Id == runtimeInstanceId
                && instance.CompetitionId == competitionId && instance.CompetitionChallengeId == competitionChallengeId
                && instance.TeamId == team.Id && instance.Purpose == RuntimePurpose.Player && instance.State == RuntimeState.Running, ct);
            return persistent ? new(competitionId, competitionChallengeId, team.Id, userId, runtimeInstanceId,
                fix.MaximumUploadBytes, RuntimePurpose.Player, FixTargetKind.Persistent) : null;
        }
        var defenseAlreadySucceeded = await db.GameplayFacts.AsNoTracking().AnyAsync(fact =>
            fact.CompetitionId == competitionId
            && fact.CompetitionChallengeId == competitionChallengeId
            && fact.TeamId == team.Id
            && fact.Kind == GameplayFactKind.FixAttempt
            && fact.State == GameplayFactState.Completed
            && fact.TimeEligibility == NoCTF.Domain.Challenges.GameplayFactTimeEligibility.Valid
            && (fact.Result == GameplayFactResult.Correct || fact.Result == GameplayFactResult.RightButDue),
            ct);
        if (defenseAlreadySucceeded)
            return null;
        var configuration = FixSubmissionConfigurationResolver.Resolve(
            available.competition.Mode,
            available.competition.ModeConfiguration!,
            available.challenge.Rules!,
            available.template.Definition!);
        if (configuration is null)
            return null;
        var expectedPurpose = RuntimePurpose.FixTarget;
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
            IsolationLevel.Serializable,
            ct);
        if (!await fileLock.AcquireAsync(db, fileId, ct))
            return new(PatchUploadSaveState.ConcurrencyConflict);

        var target = await db.RuntimeInstances.SingleOrDefaultAsync(
            instance => instance.Id == scope.RuntimeInstanceId,
            ct);
        if (scope.TargetKind == FixTargetKind.Persistent)
            return await SavePersistentAsync(patchUploadId, gameplayFactId, scope, fileId, uploadedAt, target, transaction, ct);
        if (target is null
            || target.Purpose is not (RuntimePurpose.FixTarget
                or RuntimePurpose.ScopedFixTarget)
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
            || admission.Mode != GameMode.Awdp
            || admission.CompetitionStatus != CompetitionStatus.Running)
            return new(PatchUploadSaveState.DefenseTargetNotReady);
        var eligibility = admission.Timing?.Eligibility(uploadedAt) ?? NoCTF.Domain.Challenges.GameplayFactTimeEligibility.Valid;
        if (eligibility == NoCTF.Domain.Challenges.GameplayFactTimeEligibility.NotOpened) return new(PatchUploadSaveState.ChallengeNotOpened);
        if (eligibility == NoCTF.Domain.Challenges.GameplayFactTimeEligibility.SubmissionClosed) return new(PatchUploadSaveState.ChallengeSubmissionClosed);
        if (admission.HasCorrectFix)
            return new(PatchUploadSaveState.AchievementAlreadySucceeded);
        var context = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == scope.CompetitionChallengeId)
            .Join(
                db.Challenges.AsNoTracking(),
                challenge => challenge.ChallengeId,
                template => template.Id,
                (challenge, template) => new { Challenge = challenge, Template = template })
            .AsSplitQuery()
            .SingleAsync(ct);
        var configuration = FixSubmissionConfigurationResolver.Resolve(
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
        var verificationDeadline = NoCTF.Application.Challenges.Capabilities.FixExecutionBudget.CalculateDeadline(uploadedAt, null,
            configuration.PatchTimeoutSeconds, configuration.Checker.TimeoutSeconds);
        verificationDeadline = verificationDeadline.AddTicks(-(verificationDeadline.Ticks % TimeSpan.TicksPerMicrosecond));
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
            UpdatedAt = uploadedAt,
            FixExecution = new() { GameplayFactId = gameplayFactId, RuntimeInstanceId = target.Id, TargetKind = FixTargetKind.Isolated,
                Phase = FixExecutionPhase.Queued, Deadline = verificationDeadline }
        };
        db.GameplayFacts.Add(fact);
        target.GameplayFactId = fact.Id;
        if (target.ExpiresAt is { } originalExpiry && originalExpiry < verificationDeadline) target.ExpiresAt = verificationDeadline;
        await outbox.PublishAsync(new StartFixExecution(fact.Id, target.Id));
        await events.RecordAsync(new(
            fact.CompetitionId!.Value,
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
        if (target.Purpose == RuntimePurpose.FixTarget)
        {
            await events.RecordAsync(new(
                fact.CompetitionId!.Value,
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
            replay?.Store(new AcceptedFixSubmission(patchUploadId, fact.Id, fact.State));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State, fact.ConcurrencyStamp));
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
        catch (Exception exception) when (exception is DbUpdateException
            or InvalidOperationException { InnerException: DbUpdateException }
            || TransactionFailureClassifier.IsRetryable(exception))
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

    private async Task<PatchUploadSaveResult> SavePersistentAsync(Guid uploadId, Guid factId, PatchUploadScope scope, Guid fileId,
        DateTimeOffset now, RuntimeInstance? target, IDbContextTransaction transaction, CancellationToken ct)
    {
        if (target is not { Purpose: RuntimePurpose.Player, State: RuntimeState.Running, RunnerId: not null }
            || target.CompetitionId != scope.CompetitionId || target.CompetitionChallengeId != scope.CompetitionChallengeId
            || target.TeamId != scope.TeamId || target.ExpiresAt <= now) return new(PatchUploadSaveState.DefenseTargetNotReady);
        var admission = await GameplayFactAdmissionPersistence.LoadAsync(db, scope.CompetitionId, scope.CompetitionChallengeId, scope.UserId, ct);
        if (admission is null || admission.Mode != GameMode.Awd || admission.CompetitionStatus != CompetitionStatus.Running
            || admission.CompetitionDeleted || admission.ChallengeDeleted || !admission.ChallengePublished || admission.TeamDeleted
            || admission.TeamBanned || !admission.TeamApproved || !admission.UserBelongsToTeam || admission.TeamId != scope.TeamId
            || admission.ChallengeRules is not AwdCompetitionChallengeRules { AllowFixSubmissions: true }
            || admission.ChallengeDefinition?.Fix is not { } fix) return new(PatchUploadSaveState.DefenseTargetNotReady);
        var timing = admission.Timing?.Eligibility(now) ?? GameplayFactTimeEligibility.Valid;
        if (timing == GameplayFactTimeEligibility.NotOpened) return new(PatchUploadSaveState.ChallengeNotOpened);
        if (timing == GameplayFactTimeEligibility.SubmissionClosed) return new(PatchUploadSaveState.ChallengeSubmissionClosed);
        if (await db.GameplayFacts.AnyAsync(fact => fact.FixExecution != null && fact.FixExecution.RuntimeInstanceId == target.Id
            && (fact.State == GameplayFactState.Pending || fact.State == GameplayFactState.Processing || fact.FixExecution.MutationBlocked), ct))
            return new(PatchUploadSaveState.DefenseTargetConsumed);
        var length = await db.Files.Where(file => file.Id == fileId).Select(file => file.ByteLength).SingleAsync(ct);
        if (length > fix.MaximumUploadBytes) return new(PatchUploadSaveState.DefenseTargetNotReady);
        var deadline = PersistentFixExecutionBudget.Deadline(now, fix.TimeoutSeconds);
        if (target.ExpiresAt is { } expires && expires < deadline) deadline = expires;
        deadline = deadline.AddTicks(-(deadline.Ticks % TimeSpan.TicksPerMicrosecond));
        var fact = new FixAttemptGameplayFact
        {
            Id = factId, CompetitionId = scope.CompetitionId, CompetitionChallengeId = scope.CompetitionChallengeId,
            TeamId = scope.TeamId, ActorUserId = scope.UserId, SourceIpAddress = source?.Address,
            ReferenceKind = GameplayFactReferenceKind.PatchUpload, ReferenceId = uploadId, OccurredAt = now, UpdatedAt = now,
            State = GameplayFactState.Pending, TimeEligibility = timing,
            FixExecution = new() { GameplayFactId = factId, RuntimeInstanceId = target.Id, TargetKind = FixTargetKind.Persistent,
                Phase = FixExecutionPhase.Queued, Deadline = deadline }
        };
        db.PatchUploads.Add(new() { Id = uploadId, CompetitionId = scope.CompetitionId, CompetitionChallengeId = scope.CompetitionChallengeId,
            TeamId = scope.TeamId, UploadedByUserId = scope.UserId, RuntimeInstanceId = target.Id, FileId = fileId, UploadedAt = now });
        db.GameplayFacts.Add(fact);
        target.ConcurrencyStamp = Guid.NewGuid();
        await events.RecordAsync(new(scope.CompetitionId, CompetitionEventKind.GameplayFactReceived, CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team, now, ActorUserId: scope.UserId, TeamId: scope.TeamId,
            CompetitionChallengeId: scope.CompetitionChallengeId, RuntimeInstanceId: target.Id, GameplayFactId: factId,
            GameplayFactKind: fact.Kind, GameplayFactState: fact.State), ct);
        try
        {
            replay?.Store(new AcceptedFixSubmission(uploadId, factId, fact.State));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            await outbox.PublishToRunnerNodeAsync(new RunPersistentFix(factId, target.Id, deadline, target.RunnerId));
            await outbox.PublishAsync(new GameplayFactStateChanged(factId, fact.State, fact.ConcurrencyStamp));
            await outbox.FlushCommittedMessagesAsync();
            return new(PatchUploadSaveState.Accepted, factId, fact.State);
        }
        catch (Exception exception) when (TransactionFailureClassifier.IsRetryable(exception) || exception is DbUpdateException)
        {
            await transaction.RollbackAsync(ct); outbox.DiscardPendingMessages(); db.ChangeTracker.Clear();
            return new(PatchUploadSaveState.ConcurrencyConflict);
        }
    }
}

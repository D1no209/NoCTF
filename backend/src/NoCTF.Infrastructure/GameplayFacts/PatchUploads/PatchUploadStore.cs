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
using NoCTF.GameModes.Awdp.Configuration;

namespace NoCTF.Infrastructure.GameplayFacts.PatchUploads;

public sealed class PatchUploadStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    GameplayFactAttemptCriticalSection attemptCriticalSection,
    FileReferenceLock fileLock,
    ILogger<PatchUploadStore> logger,
    ICompetitionEventRecorder? eventRecorder = null) : IPatchUploadStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public PatchUploadStore(
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ILogger<PatchUploadStore> logger)
        : this(
            db,
            outbox,
            new GameplayFactAttemptCriticalSection(
                new AsyncKeyedLock.AsyncKeyedLocker<string>()),
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
                && item.MemberIds.Contains(userId))
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
                && item.competition.Mode == GameMode.Awdp
                && item.competition.Status == CompetitionStatus.Running)
            .Select(item => new
            {
                item.competition.ConfigurationJson,
                item.challenge.RulesJson,
                item.template.DefinitionJson
            })
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
        var configuration = AwdpConfigurationResolver.Resolve(
            available.ConfigurationJson,
            available.RulesJson,
            available.DefinitionJson);
        var targetExists = await db.RuntimeInstances.AsNoTracking().AnyAsync(instance =>
            instance.Id == runtimeInstanceId
            && instance.CompetitionId == competitionId
            && instance.CompetitionChallengeId == competitionChallengeId
            && instance.TeamId == team.Id
            && instance.Purpose == RuntimePurpose.AwdpTarget,
            ct);
        return configuration.MaximumPatchUploadBytes > 0 && targetExists
            ? new(
                competitionId,
                competitionChallengeId,
                team.Id,
                userId,
                runtimeInstanceId,
                configuration.MaximumPatchUploadBytes)
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
            || target.Purpose != RuntimePurpose.AwdpTarget
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
        if (target.State != RuntimeState.Running
            || target.AwdpFixStage != AwdpFixStage.AwaitingPatch
            || target.RunnerId is null
            || target.ExpiresAt is not { } expiresAt
            || expiresAt <= uploadedAt)
        {
            return new(PatchUploadSaveState.DefenseTargetNotReady);
        }

        var admission = await GameplayFactAdmissionPersistence.LoadAsync(
            db,
            scope.CompetitionId,
            scope.CompetitionChallengeId,
            scope.UserId,
            ct);
        if (admission is null || admission.CompetitionStatus != CompetitionStatus.Running)
            return new(PatchUploadSaveState.DefenseTargetNotReady);
        if (admission.HasCorrectFix)
            return new(PatchUploadSaveState.AchievementAlreadySucceeded);
        var context = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == scope.CompetitionChallengeId)
            .Join(
                db.Challenges.AsNoTracking(),
                challenge => challenge.ChallengeId,
                template => template.Id,
                (challenge, template) => new { challenge.RulesJson, template.DefinitionJson })
            .SingleAsync(ct);
        var configuration = AwdpConfigurationResolver.Resolve(
            admission.CompetitionConfigurationJson,
            context.RulesJson,
            context.DefinitionJson);
        if (configuration.MaxFixSubmissions > 0
            && admission.AcceptedFixAttempts >= configuration.MaxFixSubmissions)
        {
            return new(PatchUploadSaveState.AttemptsExhausted);
        }
        if (configuration.RequireBreakBeforeFix && !admission.HasCorrectBreak)
            return new(PatchUploadSaveState.DefenseTargetNotReady);
        if (configuration.Checker is null || configuration.Runtime is null)
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
        var fact = new GameplayFact
        {
            Id = gameplayFactId,
            CompetitionId = scope.CompetitionId,
            CompetitionChallengeId = scope.CompetitionChallengeId,
            TeamId = scope.TeamId,
            ActorUserId = scope.UserId,
            Kind = GameplayFactKind.FixAttempt,
            ReferenceKind = GameplayFactReferenceKind.PatchUpload,
            ReferenceId = patchUploadId,
            OccurredAt = uploadedAt,
            State = GameplayFactState.Processing,
            UpdatedAt = uploadedAt
        };
        db.GameplayFacts.Add(fact);
        target.GameplayFactId = fact.Id;
        target.AwdpFixStage = AwdpFixStage.PatchApplying;
        var ttlSeconds = configuration.Runtime.TtlSeconds is > 0
            ? configuration.Runtime.TtlSeconds.Value
            : 900;
        var deadline = uploadedAt.AddSeconds(ttlSeconds);
        target.ExpiresAt = deadline;
        await LeaderboardDirty.MarkAsync(db, scope.CompetitionId, ct);
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
        await outbox.PublishToRunnerNodeAsync(new RunAwdpFixVerification(
            fact.Id,
            fact.CompetitionChallengeId,
            patchUploadId,
            target.Id,
            target.Generation,
            deadline,
            target.RunnerPool,
            target.RunnerId));
        await outbox.ScheduleAsync(new ExpireAwdpFixVerification(
            fact.Id,
            target.Id,
            target.Generation,
            deadline,
            target.RunnerPool,
            target.RunnerId), deadline);
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
            RuntimeState: target.State,
            RuntimeGeneration: target.Generation), ct);
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
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            try
            {
                await outbox.FlushOutgoingMessagesAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Patch upload {PatchUploadId} committed, but its durable cleanup message was not flushed immediately.",
                    patchUploadId);
            }
            return new(PatchUploadSaveState.Accepted, fact.Id);
        }
        catch (DbUpdateException)
        {
            return new(PatchUploadSaveState.ConcurrencyConflict);
        }
    }
}

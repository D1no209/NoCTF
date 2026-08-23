using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Infrastructure.Competitions.Koh;

public sealed class PostgresKohRuntimeProvisioner(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    IRuntimePlacementPolicy placementPolicy,
    ITransactionalMessageOutbox outbox,
    TimeProvider timeProvider,
    SharedRuntimeCriticalSection sharedRuntimeCriticalSection,
    ICompetitionEventRecorder? eventRecorder = null) : IKohRuntimeProvisioner
{
    public PostgresKohRuntimeProvisioner(
        NoCtfDbContext db,
        IChallengeRuntimeTemplateCatalog templates,
        IRuntimePlacementPolicy placementPolicy,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        ICompetitionEventRecorder? eventRecorder = null)
        : this(
            db,
            templates,
            placementPolicy,
            outbox,
            timeProvider,
            new SharedRuntimeCriticalSection(new AsyncKeyedLock.AsyncKeyedLocker<string>()),
            eventRecorder)
    { }

    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<KohRuntimeProvisioningOutcome> EnsureAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var status = await CompetitionStateReader.ReadAsync(
            db,
            competitionId,
            cancellationToken);
        if (status != CompetitionStatus.Running)
            return KohRuntimeProvisioningOutcome.RejectedBusiness;

        var mode = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.Mode)
            .SingleAsync(cancellationToken);
        if (mode != GameMode.Koh)
            return KohRuntimeProvisioningOutcome.NotApplicable;

        var challengeDefinitions = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.CompetitionId == competitionId
                && challenge.IsPublished
                && challenge.DeletedAt == null)
            .Join(
                db.Challenges.AsNoTracking(),
                challenge => challenge.ChallengeId,
                template => template.Id,
                (challenge, template) => new
                {
                    challenge.Id,
                    template.DefinitionJson
                })
            .ToListAsync(cancellationToken);
        var challenges = challengeDefinitions.Select(challenge =>
        {
            var template = templates.Get(GameMode.Koh, challenge.DefinitionJson)
                ?? throw new InvalidOperationException(
                    $"Published KoH challenge '{challenge.Id}' has no Runtime template.");
            return new
            {
                challenge.Id,
                template.RuntimeKind,
                Placement = placementPolicy.Resolve(template.RuntimeKind)
            };
        }).ToArray();
        var applied = false;
        var deferredCleanup = false;
        foreach (var challenge in challenges.OrderBy(challenge => challenge.Id))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            using var scopeLease = await sharedRuntimeCriticalSection.AcquireAsync(
                db, challenge.Id, cancellationToken);
            if (await CompetitionStateReader.ReadAsync(db, competitionId, cancellationToken)
                != CompetitionStatus.Running)
                return KohRuntimeProvisioningOutcome.RejectedBusiness;

            var existing = await db.RuntimeInstances
                .Where(runtime => runtime.CompetitionId == competitionId
                    && runtime.CompetitionChallengeId == challenge.Id
                    && runtime.Purpose == RuntimePurpose.Player
                    && runtime.TeamId == null)
                .ToListAsync(cancellationToken);
            if (existing.Any(runtime => runtime.State == RuntimeState.Stopping))
            {
                deferredCleanup = true;
                continue;
            }
            var active = existing.Any(runtime => runtime.State is RuntimeState.Queued
                or RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping);
            if (active)
                continue;

            var maximumGeneration = existing.Count == 0
                ? 0
                : existing.Max(runtime => runtime.Generation);
            var createdAt = timeProvider.GetUtcNow();
            var cleanupTarget = existing
                .Where(runtime => runtime.State == RuntimeState.Failed
                    && runtime.ProviderReceiptJson != null)
                .OrderByDescending(runtime => runtime.Generation)
                .FirstOrDefault();
            RuntimeInstance runtime;
            if (cleanupTarget is not null)
            {
                cleanupTarget.State = RuntimeState.Stopping;
                cleanupTarget.FailureCode = null;
                cleanupTarget.RunnerAssignmentReleaseToken = null;
                cleanupTarget.ProcessingVersion = checked(cleanupTarget.ProcessingVersion + 1);
                runtime = new RuntimeInstance
                {
                    Id = Guid.CreateVersion7(createdAt),
                    CompetitionId = competitionId,
                    CompetitionChallengeId = challenge.Id,
                    TeamId = null,
                    Purpose = RuntimePurpose.Player,
                    Generation = checked(maximumGeneration + 1),
                    RuntimeKind = challenge.RuntimeKind,
                    RuntimeProvider = challenge.Placement.Provider,
                    RunnerPool = challenge.Placement.RunnerPool,
                    State = RuntimeState.Queued,
                    ReplacesRuntimeInstanceId = cleanupTarget.Id,
                    CreatedAt = createdAt
                };
                await RecordStateAsync(events, cleanupTarget, CompetitionEventLevel.Warning,
                    createdAt, cancellationToken);
                await outbox.PublishAsync(new StopRuntime(
                    cleanupTarget.Id, cleanupTarget.ProcessingVersion));
                deferredCleanup = true;
            }
            else
            {
                runtime = new RuntimeInstance
                {
                    Id = Guid.CreateVersion7(createdAt),
                    CompetitionId = competitionId,
                    CompetitionChallengeId = challenge.Id,
                    TeamId = null,
                    Purpose = RuntimePurpose.Player,
                    Generation = checked(maximumGeneration + 1),
                    RuntimeKind = challenge.RuntimeKind,
                    RuntimeProvider = challenge.Placement.Provider,
                    RunnerPool = challenge.Placement.RunnerPool,
                    State = RuntimeState.Queued,
                    CreatedAt = createdAt
                };
                await outbox.PublishAsync(new DispatchRuntime(runtime.Id, runtime.ProcessingVersion));
            }
            db.RuntimeInstances.Add(runtime);
            await RecordCreatedAsync(events, runtime, createdAt, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            applied = true;
        }
        if (deferredCleanup)
            return KohRuntimeProvisioningOutcome.DeferredCleanup;
        return applied
            ? KohRuntimeProvisioningOutcome.Applied
            : KohRuntimeProvisioningOutcome.Idempotent;
    }

    private static ValueTask<Guid> RecordCreatedAsync(
        ICompetitionEventRecorder events,
        RuntimeInstance runtime,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        events.RecordAsync(new(
            runtime.CompetitionId,
            CompetitionEventKind.RuntimeCreated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Public,
            occurredAt,
            CompetitionChallengeId: runtime.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            RuntimeState: runtime.State,
            RuntimeGeneration: runtime.Generation), cancellationToken);

    private static ValueTask<Guid> RecordStateAsync(
        ICompetitionEventRecorder events,
        RuntimeInstance runtime,
        CompetitionEventLevel level,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        events.RecordAsync(new(
            runtime.CompetitionId,
            CompetitionEventKind.RuntimeStateChanged,
            level,
            CompetitionEventVisibility.Public,
            occurredAt,
            CompetitionChallengeId: runtime.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            RuntimeState: runtime.State,
            RuntimeGeneration: runtime.Generation), cancellationToken);
}

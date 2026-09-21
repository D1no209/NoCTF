using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Infrastructure.Competitions.Awd;

public sealed class PostgresAwdRuntimeProvisioner(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    IRuntimePlacementPolicy placementPolicy,
    ITransactionalMessageOutbox outbox,
    TimeProvider timeProvider,
    TeamRuntimeQuota runtimeQuota,
    ICompetitionEventRecorder? eventRecorder = null) : IAwdRuntimeProvisioner
{
    public PostgresAwdRuntimeProvisioner(
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
            new TeamRuntimeQuota(new AsyncKeyedLock.AsyncKeyedLocker<string>()),
            eventRecorder)
    { }

    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<AwdRuntimeProvisioningOutcome> EnsureAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var status = await CompetitionStateReader.ReadAsync(
            db,
            competitionId,
            cancellationToken);
        if (status != CompetitionStatus.Running)
            return AwdRuntimeProvisioningOutcome.RejectedBusiness;

        var competition = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => new
            {
                competition.Mode,
                competition.MaxConcurrentRuntimeInstancesPerTeam,
                competition.RuntimeAccessMode,
                competition.TrafficCaptureEnabled,
                competition.TrafficCaptureLimitBytes
            })
            .SingleAsync(cancellationToken);
        if (competition.Mode != GameMode.Awd)
            return AwdRuntimeProvisioningOutcome.NotApplicable;

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
            var template = templates.Get(GameMode.Awd, challenge.DefinitionJson)
                ?? throw new InvalidOperationException(
                    $"Published AWD challenge '{challenge.Id}' has no Runtime template.");
            return new
            {
                challenge.Id,
                template.RuntimeKind,
                Placement = placementPolicy.Resolve(template.RuntimeKind)
            };
        }).ToArray();
        var teamIds = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned
                && team.DeletedAt == null)
            .Select(team => team.Id)
            .Order()
            .ToListAsync(cancellationToken);
        var desiredChallengeIds = challenges.Select(challenge => challenge.Id).ToHashSet();
        var applied = false;
        var deferredCleanup = false;
        AwdRuntimeProvisioningOutcome? terminalOutcome = null;
        foreach (var teamId in teamIds)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            using var quotaLease = await runtimeQuota.AcquireLockAsync(
                db,
                competitionId,
                teamId,
                cancellationToken);
            if (await CompetitionStateReader.ReadAsync(db, competitionId, cancellationToken)
                != CompetitionStatus.Running)
            {
                terminalOutcome = AwdRuntimeProvisioningOutcome.RejectedBusiness;
                break;
            }

            var existing = await db.RuntimeInstances
                .Where(runtime => runtime.CompetitionId == competitionId
                    && runtime.Purpose == RuntimePurpose.Player
                    && runtime.TeamId == teamId)
                .ToListAsync(cancellationToken);
            if (existing.Any(runtime => runtime.CompetitionChallengeId is Guid runtimeChallengeId
                    && desiredChallengeIds.Contains(runtimeChallengeId)
                    && runtime.State == RuntimeState.Stopping))
            {
                deferredCleanup = true;
                continue;
            }

            var active = existing
                .Where(runtime => runtime.State is RuntimeState.Queued
                    or RuntimeState.Provisioning
                    or RuntimeState.Running
                    or RuntimeState.Stopping)
                .Select(runtime => runtime.CompetitionChallengeId!.Value)
                .ToHashSet();
            if (competition.MaxConcurrentRuntimeInstancesPerTeam > 0
                && active.Concat(desiredChallengeIds).Distinct().Count()
                    > competition.MaxConcurrentRuntimeInstancesPerTeam)
            {
                terminalOutcome = AwdRuntimeProvisioningOutcome.CapacityExceeded;
                break;
            }

            var createdAt = timeProvider.GetUtcNow();
            var cleanupTargets = existing
                .Where(runtime => runtime.State == RuntimeState.Failed
                    && runtime.ProviderReceiptJson != null
                    && runtime.CompetitionChallengeId is Guid runtimeChallengeId
                    && desiredChallengeIds.Contains(runtimeChallengeId))
                .GroupBy(runtime => runtime.CompetitionChallengeId!.Value)
                .Where(group => !active.Contains(group.Key))
                .Select(group => group
                    .OrderByDescending(runtime => runtime.CreatedAt)
                    .ThenByDescending(runtime => runtime.Id)
                    .First())
                .ToList();
            if (cleanupTargets.Count > 0)
            {
                var challengesById = challenges.ToDictionary(challenge => challenge.Id);
                foreach (var cleanupTarget in cleanupTargets)
                {
                    var challenge = challengesById[cleanupTarget.CompetitionChallengeId!.Value];
                    cleanupTarget.State = RuntimeState.Stopping;
                    cleanupTarget.FailureCode = null;
                    var replacement = new RuntimeInstance
                    {
                        Id = Guid.CreateVersion7(createdAt),
                        CompetitionId = competitionId,
                        CompetitionChallengeId = cleanupTarget.CompetitionChallengeId,
                        TeamId = teamId,
                        Purpose = RuntimePurpose.Player,
                        RuntimeKind = challenge.RuntimeKind,
                        RuntimeProvider = challenge.Placement.Provider,
                        AccessMode = competition.RuntimeAccessMode,
                        TrafficCaptureEnabled = competition.TrafficCaptureEnabled,
                        TrafficCaptureLimitBytes = competition.TrafficCaptureLimitBytes,
                        State = RuntimeState.Queued,
                        CreatedAt = createdAt
                    };
                    db.RuntimeInstances.Add(replacement);
                    await RecordCreatedAsync(events, replacement, createdAt, cancellationToken);
                    await RecordStateAsync(events, cleanupTarget, CompetitionEventLevel.Warning,
                        createdAt, cancellationToken);
                    await outbox.PublishAsync(new StopRuntime(cleanupTarget.Id));
                    await outbox.PublishAsync(new DispatchRuntime(replacement.Id));
                }
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                deferredCleanup = true;
                applied = true;
                continue;
            }

            var created = new List<RuntimeInstance>();
            foreach (var challenge in challenges)
            {
                if (active.Contains(challenge.Id))
                    continue;
                var runtime = new RuntimeInstance
                {
                    Id = Guid.CreateVersion7(createdAt),
                    CompetitionId = competitionId,
                    CompetitionChallengeId = challenge.Id,
                    TeamId = teamId,
                    Purpose = RuntimePurpose.Player,
                    RuntimeKind = challenge.RuntimeKind,
                    RuntimeProvider = challenge.Placement.Provider,
                    AccessMode = competition.RuntimeAccessMode,
                    TrafficCaptureEnabled = competition.TrafficCaptureEnabled,
                    TrafficCaptureLimitBytes = competition.TrafficCaptureLimitBytes,
                    State = RuntimeState.Queued,
                    CreatedAt = createdAt
                };
                db.RuntimeInstances.Add(runtime);
                created.Add(runtime);
            }
            if (created.Count == 0)
                continue;
            foreach (var runtime in created)
            {
                await outbox.PublishAsync(new DispatchRuntime(runtime.Id));
                await RecordCreatedAsync(events, runtime, createdAt, cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            applied = true;
        }
        if (applied)
            await outbox.FlushOutgoingMessagesAsync();
        if (terminalOutcome is not null)
            return terminalOutcome.Value;
        if (deferredCleanup)
            return AwdRuntimeProvisioningOutcome.DeferredCleanup;
        return applied
            ? AwdRuntimeProvisioningOutcome.Applied
            : AwdRuntimeProvisioningOutcome.Idempotent;
    }

    private static ValueTask<Guid> RecordCreatedAsync(
        ICompetitionEventRecorder events,
        RuntimeInstance runtime,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        events.RecordAsync(new(
            runtime.CompetitionId!.Value,
            CompetitionEventKind.RuntimeCreated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            occurredAt,
            TeamId: runtime.TeamId,
            CompetitionChallengeId: runtime.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            RuntimeState: runtime.State), cancellationToken);

    private static ValueTask<Guid> RecordStateAsync(
        ICompetitionEventRecorder events,
        RuntimeInstance runtime,
        CompetitionEventLevel level,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        events.RecordAsync(new(
            runtime.CompetitionId!.Value,
            CompetitionEventKind.RuntimeStateChanged,
            level,
            CompetitionEventVisibility.Team,
            occurredAt,
            TeamId: runtime.TeamId,
            CompetitionChallengeId: runtime.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            RuntimeState: runtime.State), cancellationToken);
}

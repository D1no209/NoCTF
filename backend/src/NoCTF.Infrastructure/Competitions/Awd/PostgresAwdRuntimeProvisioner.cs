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
    ICompetitionEventRecorder? eventRecorder = null) : IAwdRuntimeProvisioner
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<AwdRuntimeProvisioningOutcome> EnsureAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
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
                competition.MaxConcurrentRuntimeInstancesPerTeam
            })
            .SingleAsync(cancellationToken);
        if (competition.Mode != GameMode.Awd)
            return AwdRuntimeProvisioningOutcome.NotApplicable;

        var challenges = await db.CompetitionChallenges.AsNoTracking()
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
        var teamIds = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned
                && team.DeletedAt == null)
            .Select(team => team.Id)
            .Order()
            .ToListAsync(cancellationToken);
        await using var quotaLeases = new CriticalSectionLeaseCollection();
        foreach (var teamId in teamIds)
        {
            quotaLeases.Add(await TeamRuntimeQuota.AcquireLockAsync(
                db,
                competitionId,
                teamId,
                cancellationToken));
        }
        var challengeIds = challenges.Select(challenge => challenge.Id).ToArray();
        var existing = await db.RuntimeInstances
            .Where(runtime => runtime.CompetitionId == competitionId
                && runtime.Purpose == RuntimePurpose.Player
                && runtime.TeamId != null
                && teamIds.Contains(runtime.TeamId.Value))
            .ToListAsync(cancellationToken);
        var desiredChallengeIds = challengeIds.ToHashSet();
        if (existing.Any(runtime =>
                desiredChallengeIds.Contains(runtime.CompetitionChallengeId)
                && runtime.State == RuntimeState.Stopping))
            return AwdRuntimeProvisioningOutcome.DeferredCleanup;
        var active = existing
            .Where(runtime => runtime.State is RuntimeState.Queued
                or RuntimeState.Provisioning
                or RuntimeState.Running
                or RuntimeState.Stopping)
            .Select(runtime => (
                runtime.CompetitionChallengeId,
                TeamId: runtime.TeamId!.Value))
            .ToHashSet();
        if (competition.MaxConcurrentRuntimeInstancesPerTeam > 0)
        {
            var exceedsQuota = teamIds.Any(teamId =>
                active
                    .Where(runtime => runtime.TeamId == teamId)
                    .Select(runtime => runtime.CompetitionChallengeId)
                    .Concat(challengeIds)
                    .Distinct()
                    .Count() > competition.MaxConcurrentRuntimeInstancesPerTeam);
            if (exceedsQuota)
                return AwdRuntimeProvisioningOutcome.CapacityExceeded;
        }
        var maximumGenerations = existing
            .GroupBy(runtime => (
                runtime.CompetitionChallengeId,
                TeamId: runtime.TeamId!.Value))
            .ToDictionary(group => group.Key, group => group.Max(runtime => runtime.Generation));

        var createdAt = timeProvider.GetUtcNow();
        var cleanupTargets = existing
            .Where(runtime => runtime.State == RuntimeState.Failed
                && runtime.ProviderReceiptJson != null
                && desiredChallengeIds.Contains(runtime.CompetitionChallengeId))
            .GroupBy(runtime => (
                runtime.CompetitionChallengeId,
                TeamId: runtime.TeamId!.Value))
            .Where(group => !active.Contains(group.Key))
            .Select(group => group
                .OrderByDescending(runtime => runtime.Generation)
                .First())
            .ToList();
        if (cleanupTargets.Count > 0)
        {
            var challengesById = challenges.ToDictionary(challenge => challenge.Id);
            foreach (var cleanupTarget in cleanupTargets)
            {
                var key = (
                    cleanupTarget.CompetitionChallengeId,
                    TeamId: cleanupTarget.TeamId!.Value);
                var challenge = challengesById[cleanupTarget.CompetitionChallengeId];
                var template = templates.Get(GameMode.Awd, challenge.DefinitionJson)
                    ?? throw new InvalidOperationException(
                        $"Published AWD challenge '{challenge.Id}' has no Runtime template.");
                var placement = placementPolicy.Resolve(template.RuntimeKind);
                cleanupTarget.State = RuntimeState.Stopping;
                cleanupTarget.FailureCode = null;
                cleanupTarget.RunnerAssignmentReleaseToken = null;
                cleanupTarget.ProcessingVersion = checked(cleanupTarget.ProcessingVersion + 1);
                var replacement = new RuntimeInstance
                {
                    Id = Guid.CreateVersion7(createdAt),
                    CompetitionId = competitionId,
                    CompetitionChallengeId = cleanupTarget.CompetitionChallengeId,
                    TeamId = cleanupTarget.TeamId,
                    Purpose = RuntimePurpose.Player,
                    Generation = checked(maximumGenerations.GetValueOrDefault(key) + 1),
                    RuntimeKind = template.RuntimeKind,
                    RuntimeProvider = placement.Provider,
                    RunnerPool = placement.RunnerPool,
                    State = RuntimeState.Queued,
                    ReplacesRuntimeInstanceId = cleanupTarget.Id,
                    CreatedAt = createdAt
                };
                db.RuntimeInstances.Add(replacement);
                await RecordCreatedAsync(events, replacement, createdAt, cancellationToken);
                await RecordStateAsync(
                    events,
                    cleanupTarget,
                    CompetitionEventLevel.Warning,
                    createdAt,
                    cancellationToken);
                await outbox.PublishAsync(new StopRuntime(
                    cleanupTarget.Id,
                    cleanupTarget.ProcessingVersion));
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return AwdRuntimeProvisioningOutcome.DeferredCleanup;
        }

        var created = new List<RuntimeInstance>();
        foreach (var challenge in challenges)
        {
            var template = templates.Get(GameMode.Awd, challenge.DefinitionJson)
                ?? throw new InvalidOperationException(
                    $"Published AWD challenge '{challenge.Id}' has no Runtime template.");
            foreach (var teamId in teamIds)
            {
                var key = (challenge.Id, teamId);
                if (active.Contains(key))
                    continue;
                var placement = placementPolicy.Resolve(template.RuntimeKind);
                var runtime = new RuntimeInstance
                {
                    Id = Guid.CreateVersion7(createdAt),
                    CompetitionId = competitionId,
                    CompetitionChallengeId = challenge.Id,
                    TeamId = teamId,
                    Purpose = RuntimePurpose.Player,
                    Generation = checked(maximumGenerations.GetValueOrDefault(key) + 1),
                    RuntimeKind = template.RuntimeKind,
                    RuntimeProvider = placement.Provider,
                    RunnerPool = placement.RunnerPool,
                    State = RuntimeState.Queued,
                    CreatedAt = createdAt
                };
                db.RuntimeInstances.Add(runtime);
                created.Add(runtime);
            }
        }

        if (created.Count == 0)
            return AwdRuntimeProvisioningOutcome.Idempotent;
        foreach (var runtime in created)
        {
            await outbox.PublishAsync(new DispatchRuntime(runtime.Id, runtime.ProcessingVersion));
            await RecordCreatedAsync(events, runtime, createdAt, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return AwdRuntimeProvisioningOutcome.Applied;
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
            CompetitionEventVisibility.Team,
            occurredAt,
            TeamId: runtime.TeamId,
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
            CompetitionEventVisibility.Team,
            occurredAt,
            TeamId: runtime.TeamId,
            CompetitionChallengeId: runtime.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            RuntimeState: runtime.State,
            RuntimeGeneration: runtime.Generation), cancellationToken);
}

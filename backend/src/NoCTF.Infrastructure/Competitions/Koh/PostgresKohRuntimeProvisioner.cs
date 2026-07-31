using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Instances;

namespace NoCTF.Infrastructure.Competitions.Koh;

public sealed class PostgresKohRuntimeProvisioner(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    IRuntimePlacementPolicy placementPolicy,
    ITransactionalMessageOutbox outbox,
    TimeProvider timeProvider) : IKohRuntimeProvisioner
{
    public async Task<KohRuntimeProvisioningOutcome> EnsureAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var status = await CompetitionWriteLock.AcquireAsync(
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
        var challengeIds = challenges.Select(challenge => challenge.Id).ToArray();
        foreach (var challengeId in challengeIds.Order())
        {
            await SharedRuntimeScopeLock.AcquireAsync(
                db,
                challengeId,
                cancellationToken);
        }
        var existing = await db.RuntimeInstances
            .Where(runtime => runtime.CompetitionId == competitionId
                && runtime.Purpose == RuntimePurpose.Player
                && challengeIds.Contains(runtime.CompetitionChallengeId)
                && runtime.TeamId == null)
            .ToListAsync(cancellationToken);
        if (existing.Any(runtime => runtime.State == RuntimeState.Stopping))
            return KohRuntimeProvisioningOutcome.DeferredCleanup;
        var active = existing
            .Where(runtime => runtime.State is RuntimeState.Queued
                or RuntimeState.Provisioning
                or RuntimeState.Running
                or RuntimeState.Stopping)
            .Select(runtime => runtime.CompetitionChallengeId)
            .ToHashSet();
        var maximumGenerations = existing
            .GroupBy(runtime => runtime.CompetitionChallengeId)
            .ToDictionary(
                group => group.Key,
                group => group.Max(runtime => runtime.Generation));

        var createdAt = timeProvider.GetUtcNow();
        var cleanupTargets = existing
            .Where(runtime => runtime.State == RuntimeState.Failed
                && runtime.ProviderReceiptJson != null)
            .GroupBy(runtime => runtime.CompetitionChallengeId)
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
                var challenge = challengesById[cleanupTarget.CompetitionChallengeId];
                var template = templates.Get(GameMode.Koh, challenge.DefinitionJson)
                    ?? throw new InvalidOperationException(
                        $"Published KoH challenge '{challenge.Id}' has no Runtime template.");
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
                    TeamId = null,
                    Purpose = RuntimePurpose.Player,
                    Generation = checked(maximumGenerations.GetValueOrDefault(
                        cleanupTarget.CompetitionChallengeId) + 1),
                    RuntimeKind = template.RuntimeKind,
                    RuntimeProvider = placement.Provider,
                    RunnerPool = placement.RunnerPool,
                    State = RuntimeState.Queued,
                    ReplacesRuntimeInstanceId = cleanupTarget.Id,
                    CreatedAt = createdAt
                };
                db.RuntimeInstances.Add(replacement);
                await outbox.PublishAsync(new StopRuntime(
                    cleanupTarget.Id,
                    cleanupTarget.ProcessingVersion));
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return KohRuntimeProvisioningOutcome.DeferredCleanup;
        }

        var created = new List<RuntimeInstance>();
        foreach (var challenge in challenges)
        {
            if (active.Contains(challenge.Id))
                continue;
            var template = templates.Get(GameMode.Koh, challenge.DefinitionJson)
                ?? throw new InvalidOperationException(
                    $"Published KoH challenge '{challenge.Id}' has no Runtime template.");
            var placement = placementPolicy.Resolve(template.RuntimeKind);
            var runtime = new RuntimeInstance
            {
                Id = Guid.CreateVersion7(createdAt),
                CompetitionId = competitionId,
                CompetitionChallengeId = challenge.Id,
                TeamId = null,
                Purpose = RuntimePurpose.Player,
                Generation = checked(maximumGenerations.GetValueOrDefault(challenge.Id) + 1),
                RuntimeKind = template.RuntimeKind,
                RuntimeProvider = placement.Provider,
                RunnerPool = placement.RunnerPool,
                State = RuntimeState.Queued,
                CreatedAt = createdAt
            };
            db.RuntimeInstances.Add(runtime);
            created.Add(runtime);
        }

        if (created.Count == 0)
            return KohRuntimeProvisioningOutcome.Idempotent;
        foreach (var runtime in created)
            await outbox.PublishAsync(new DispatchRuntime(runtime.Id, runtime.ProcessingVersion));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return KohRuntimeProvisioningOutcome.Applied;
    }
}

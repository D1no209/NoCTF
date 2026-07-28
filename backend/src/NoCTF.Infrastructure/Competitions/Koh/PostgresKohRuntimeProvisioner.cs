using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Koh;

public sealed class PostgresKohRuntimeProvisioner(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
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
            .Select(challenge => new
            {
                challenge.Id,
                challenge.ConfigurationJson,
                challenge.Revision
            })
            .ToListAsync(cancellationToken);
        var challengeIds = challenges.Select(challenge => challenge.Id).ToArray();
        var existing = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => challengeIds.Contains(runtime.CompetitionChallengeId)
                && runtime.TeamId == null)
            .Select(runtime => new
            {
                runtime.CompetitionChallengeId,
                runtime.Generation,
                runtime.State
            })
            .ToListAsync(cancellationToken);
        var active = existing
            .Where(runtime => runtime.State is RuntimeState.Queued
                or RuntimeState.Provisioning
                or RuntimeState.Running)
            .Select(runtime => runtime.CompetitionChallengeId)
            .ToHashSet();
        var maximumGenerations = existing
            .GroupBy(runtime => runtime.CompetitionChallengeId)
            .ToDictionary(
                group => group.Key,
                group => group.Max(runtime => runtime.Generation));

        var createdAt = timeProvider.GetUtcNow();
        var created = new List<RuntimeInstance>();
        foreach (var challenge in challenges)
        {
            if (active.Contains(challenge.Id))
                continue;
            var template = templates.Get(GameMode.Koh, challenge.ConfigurationJson)
                ?? throw new InvalidOperationException(
                    $"Published KoH challenge '{challenge.Id}' has no Runtime template.");
            var runtime = new RuntimeInstance
            {
                Id = Guid.CreateVersion7(createdAt),
                CompetitionId = competitionId,
                CompetitionChallengeId = challenge.Id,
                TeamId = null,
                Purpose = RuntimePurpose.Player,
                Generation = checked(maximumGenerations.GetValueOrDefault(challenge.Id) + 1),
                RuntimeKind = template.RuntimeKind,
                RuntimeProvider = template.Provider,
                RunnerPool = template.RunnerPool,
                State = RuntimeState.Queued,
                ConfigurationRevision = challenge.Revision,
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

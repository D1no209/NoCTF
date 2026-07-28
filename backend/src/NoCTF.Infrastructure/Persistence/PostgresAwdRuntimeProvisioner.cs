using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence;

public sealed class PostgresAwdRuntimeProvisioner(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    ITransactionalMessageOutbox outbox,
    TimeProvider timeProvider) : IAwdRuntimeProvisioner
{
    public async Task<AwdRuntimeProvisioningOutcome> EnsureAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var status = await CompetitionWriteLock.AcquireAsync(
            db,
            competitionId,
            cancellationToken);
        if (status != CompetitionStatus.Running)
            return AwdRuntimeProvisioningOutcome.RejectedBusiness;

        var mode = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.Mode)
            .SingleAsync(cancellationToken);
        if (mode != GameMode.Awd)
            return AwdRuntimeProvisioningOutcome.NotApplicable;

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
        var teamIds = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned
                && team.DeletedAt == null)
            .Select(team => team.Id)
            .ToListAsync(cancellationToken);
        var challengeIds = challenges.Select(challenge => challenge.Id).ToArray();
        var existing = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => challengeIds.Contains(runtime.CompetitionChallengeId)
                && runtime.TeamId != null)
            .Select(runtime => new
            {
                runtime.CompetitionChallengeId,
                TeamId = runtime.TeamId!.Value,
                runtime.Generation,
                runtime.State
            })
            .ToListAsync(cancellationToken);
        var active = existing
            .Where(runtime => runtime.State is RuntimeState.Queued
                or RuntimeState.Provisioning
                or RuntimeState.Running)
            .Select(runtime => (runtime.CompetitionChallengeId, runtime.TeamId))
            .ToHashSet();
        var maximumGenerations = existing
            .GroupBy(runtime => (runtime.CompetitionChallengeId, runtime.TeamId))
            .ToDictionary(group => group.Key, group => group.Max(runtime => runtime.Generation));

        var createdAt = timeProvider.GetUtcNow();
        var created = new List<RuntimeInstance>();
        foreach (var challenge in challenges)
        {
            var template = templates.Get(GameMode.Awd, challenge.ConfigurationJson)
                ?? throw new InvalidOperationException(
                    $"Published AWD challenge '{challenge.Id}' has no Runtime template.");
            foreach (var teamId in teamIds)
            {
                var key = (challenge.Id, teamId);
                if (active.Contains(key))
                    continue;
                var runtime = new RuntimeInstance
                {
                    Id = Guid.CreateVersion7(createdAt),
                    CompetitionId = competitionId,
                    CompetitionChallengeId = challenge.Id,
                    TeamId = teamId,
                    Purpose = RuntimePurpose.Player,
                    Generation = checked(maximumGenerations.GetValueOrDefault(key) + 1),
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
        }

        if (created.Count == 0)
            return AwdRuntimeProvisioningOutcome.Idempotent;
        foreach (var runtime in created)
            await outbox.PublishAsync(new DispatchRuntime(runtime.Id, runtime.ProcessingVersion));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return AwdRuntimeProvisioningOutcome.Applied;
    }
}

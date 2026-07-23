using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfAdminRuntimeStore(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    ITransactionalMessageOutbox outbox) : IAdminRuntimeStore
{
    public async Task<IReadOnlyList<RuntimeInstanceView>> ListAsync(
        AdminRuntimeFilter filter,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct)
    {
        var query = db.RuntimeInstances.AsNoTracking()
            .Where(item => item.CompetitionId == filter.CompetitionId);
        if (filter.CompetitionChallengeId is Guid challengeId)
            query = query.Where(item => item.CompetitionChallengeId == challengeId);
        if (filter.TeamId is Guid teamId)
            query = query.Where(item => item.TeamId == teamId);
        if (filter.RuntimeKind is RuntimeKind kind)
            query = query.Where(item => item.RuntimeKind == kind);
        if (filter.Provider is RuntimeProvider provider)
            query = query.Where(item => item.RuntimeProvider == provider);
        if (!string.IsNullOrWhiteSpace(filter.RunnerPool))
            query = query.Where(item => item.RunnerPool == filter.RunnerPool);
        if (!string.IsNullOrWhiteSpace(filter.RunnerId))
            query = query.Where(item => item.RunnerId == filter.RunnerId);
        if (filter.State is RuntimeState state)
            query = query.Where(item => item.State == state);
        if (filter.ExpiresBefore is DateTimeOffset expires)
            query = query.Where(item => item.ExpiresAt < expires);
        if (beforeCreatedAt is DateTimeOffset createdAt && beforeId is Guid id)
            query = query.Where(item =>
                item.CreatedAt < createdAt ||
                item.CreatedAt == createdAt && item.Id.CompareTo(id) < 0);
        return await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(limit)
            .Select(item => new RuntimeInstanceView(
                item.Id, item.CompetitionId, item.CompetitionChallengeId, item.TeamId,
                item.Generation, item.RuntimeKind, item.RuntimeProvider, item.RunnerPool,
                item.State, item.FailureCode, item.ProcessingVersion, item.Urls,
                item.CreatedAt, item.RunningAt, item.ExpiresAt, item.StoppedAt,
                item.RunnerId, item.ProviderReceiptJson, item.ControlCheckUrl))
            .ToListAsync(ct);
    }

    public Task<RuntimeInstanceView?> FindAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        CancellationToken ct) =>
        db.RuntimeInstances.AsNoTracking()
            .Where(item => item.Id == runtimeInstanceId && item.CompetitionId == competitionId)
            .Select(item => new RuntimeInstanceView(
                item.Id, item.CompetitionId, item.CompetitionChallengeId, item.TeamId,
                item.Generation, item.RuntimeKind, item.RuntimeProvider, item.RunnerPool,
                item.State, item.FailureCode, item.ProcessingVersion, item.Urls,
                item.CreatedAt, item.RunningAt, item.ExpiresAt, item.StoppedAt,
                item.RunnerId, item.ProviderReceiptJson, item.ControlCheckUrl))
            .SingleOrDefaultAsync(ct);

    public async Task<RuntimeMutationResult> MutateAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid? teamId,
        RuntimeAction action,
        TimeSpan? extension,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var scope = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge =>
                challenge.Id == competitionChallengeId &&
                challenge.CompetitionId == competitionId)
            .Join(
                db.Competitions.AsNoTracking(),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new
                {
                    Competition = competition,
                    Challenge = challenge
                })
            .SingleOrDefaultAsync(ct);
        if (scope is null || scope.Competition.Status != CompetitionStatus.Running)
            return new(null, RuntimeMutationFailure.NotFound);
        if ((teamId is null) != (scope.Competition.Mode == GameMode.Koh))
            return new(null, RuntimeMutationFailure.Unsupported);
        if (teamId is not null && scope.Competition.Mode is not (GameMode.Ctf or GameMode.Awd))
            return new(null, RuntimeMutationFailure.Unsupported);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({competitionChallengeId.ToString() + ":" + (teamId?.ToString() ?? "shared")}, 0))",
            ct);
        var current = await db.RuntimeInstances
            .Where(item =>
                item.CompetitionChallengeId == competitionChallengeId &&
                item.TeamId == teamId)
            .OrderByDescending(item => item.Generation)
            .FirstOrDefaultAsync(ct);
        RuntimeInstance entity;
        if (action is RuntimeAction.Start or RuntimeAction.Reset)
        {
            if (action == RuntimeAction.Start && current is not null && IsActive(current.State))
                return new(null, RuntimeMutationFailure.InvalidState);
            if (action == RuntimeAction.Reset && (current is null || !IsActive(current.State)))
                return new(null, RuntimeMutationFailure.InvalidState);
            if (current is not null && action == RuntimeAction.Reset)
            {
                current.State = RuntimeState.Stopping;
                current.ProcessingVersion = checked(current.ProcessingVersion + 1);
                await outbox.PublishAsync(new StopRuntime(current.Id, current.ProcessingVersion));
            }
            var template = templates.Get(scope.Competition.Mode, scope.Challenge.ConfigurationJson);
            if (template is null)
                return new(null, RuntimeMutationFailure.ConfigurationInvalid);
            entity = new RuntimeInstance
            {
                Id = Guid.CreateVersion7(now),
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = teamId,
                Generation = current is null ? 0 : checked(current.Generation + 1),
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = template.Provider,
                RunnerPool = "default",
                State = RuntimeState.Queued,
                ConfigurationRevision = scope.Challenge.Revision,
                ReplacesRuntimeInstanceId = action == RuntimeAction.Reset ? current?.Id : null,
                CreatedAt = now,
                ExpiresAt = template.TtlSeconds is > 0 ? now.AddSeconds(template.TtlSeconds.Value) : null
            };
            db.RuntimeInstances.Add(entity);
            await outbox.PublishAsync(new DispatchRuntime(entity.Id, entity.ProcessingVersion));
        }
        else if (action == RuntimeAction.Stop)
        {
            if (current is null || !IsActive(current.State))
                return new(null, RuntimeMutationFailure.InvalidState);
            current.State = RuntimeState.Stopping;
            current.ProcessingVersion = checked(current.ProcessingVersion + 1);
            entity = current;
            await outbox.PublishAsync(new StopRuntime(entity.Id, entity.ProcessingVersion));
        }
        else if (action == RuntimeAction.Extend)
        {
            if (teamId is null || current is null || current.State != RuntimeState.Running ||
                current.ExpiresAt is null || extension is null || extension <= TimeSpan.Zero)
                return new(null, RuntimeMutationFailure.InvalidState);
            current.ExpiresAt = current.ExpiresAt.Value.Add(extension.Value);
            current.ProcessingVersion = checked(current.ProcessingVersion + 1);
            entity = current;
        }
        else
        {
            return new(null, RuntimeMutationFailure.Unsupported);
        }
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushOutgoingMessagesAsync();
            return new(Map(entity));
        }
        catch (DbUpdateException)
        {
            return new(null, RuntimeMutationFailure.Conflict);
        }
    }

    private static bool IsActive(RuntimeState state) =>
        state is RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping;

    private static RuntimeInstanceView Map(RuntimeInstance item) =>
        new(
            item.Id, item.CompetitionId, item.CompetitionChallengeId, item.TeamId,
            item.Generation, item.RuntimeKind, item.RuntimeProvider, item.RunnerPool,
            item.State, item.FailureCode, item.ProcessingVersion, item.Urls,
            item.CreatedAt, item.RunningAt, item.ExpiresAt, item.StoppedAt,
            item.RunnerId, item.ProviderReceiptJson, item.ControlCheckUrl);
}

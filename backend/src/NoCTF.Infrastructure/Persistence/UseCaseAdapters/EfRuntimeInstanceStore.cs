using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfRuntimeInstanceStore(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    IPerTeamRuntimeFlagStore runtimeFlags,
    ITransactionalMessageOutbox outbox) : IRuntimeInstanceStore
{
    public async Task<RuntimeInstanceView?> FindPlayerRuntimeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(competitionId, competitionChallengeId, userId, ct);
        if (scope is null || scope.Mode is GameMode.Koh or GameMode.Awdp)
            return null;
        return await db.RuntimeInstances.AsNoTracking()
            .Where(instance =>
                instance.CompetitionId == competitionId &&
                instance.CompetitionChallengeId == competitionChallengeId &&
                instance.Purpose == RuntimePurpose.Player &&
                instance.TeamId == scope.TeamId)
            .OrderByDescending(instance => instance.Generation)
            .Select(instance => new RuntimeInstanceView(
                instance.Id, instance.CompetitionId, instance.CompetitionChallengeId, instance.TeamId,
                instance.Generation, instance.RuntimeKind, instance.RuntimeProvider, instance.RunnerPool,
                instance.State, instance.FailureCode, instance.ProcessingVersion, instance.Urls,
                instance.CreatedAt, instance.RunningAt, instance.ExpiresAt, instance.StoppedAt))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<RuntimeMutationResult> MutatePlayerRuntimeAsync(
        RuntimeMutationCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var scope = await ResolveScopeAsync(
            command.CompetitionId,
            command.CompetitionChallengeId,
            command.UserId,
            ct);
        if (scope is null || scope.Status != CompetitionStatus.Running)
            return new(null, RuntimeMutationFailure.NotFound);
        if (scope.Mode is GameMode.Koh or GameMode.Awdp)
            return new(null, RuntimeMutationFailure.Unsupported);
        if (scope.Mode == GameMode.Awd && command.Action is RuntimeAction.Start or RuntimeAction.Stop or RuntimeAction.Extend)
            return new(null, RuntimeMutationFailure.Unsupported);

        await AcquireLockAsync(scope.TeamId, command.CompetitionChallengeId, ct);
        var current = await db.RuntimeInstances
            .Where(instance =>
                instance.CompetitionChallengeId == command.CompetitionChallengeId &&
                instance.Purpose == RuntimePurpose.Player &&
                instance.TeamId == scope.TeamId)
            .OrderByDescending(instance => instance.Generation)
            .FirstOrDefaultAsync(ct);

        RuntimeInstance entity;
        switch (command.Action)
        {
            case RuntimeAction.Start:
                if (current is not null && IsActive(current.State))
                    return new(null, RuntimeMutationFailure.InvalidState);
                var replacesFailed = current is { State: RuntimeState.Failed, ProviderReceiptJson: not null };
                if (replacesFailed)
                {
                    current!.State = RuntimeState.Stopping;
                    current.ProcessingVersion = checked(current.ProcessingVersion + 1);
                    await outbox.PublishAsync(new StopRuntime(current.Id, current.ProcessingVersion));
                }
                try
                {
                    entity = await CreateAsync(
                        scope,
                        command,
                        checked((current?.Generation ?? 0) + 1),
                        replacesFailed ? current!.Id : null,
                        ct);
                }
                catch (InvalidOperationException) { return new(null, RuntimeMutationFailure.ConfigurationInvalid); }
                db.RuntimeInstances.Add(entity);
                if (!replacesFailed)
                    await outbox.PublishAsync(new DispatchRuntime(entity.Id, entity.ProcessingVersion));
                break;
            case RuntimeAction.Reset:
                if (current is null || !IsActive(current.State))
                    return new(null, RuntimeMutationFailure.InvalidState);
                current.State = RuntimeState.Stopping;
                current.ProcessingVersion = checked(current.ProcessingVersion + 1);
                await outbox.PublishAsync(new StopRuntime(current.Id, current.ProcessingVersion));
                try
                {
                    entity = await CreateAsync(
                        scope,
                        command,
                        checked(current.Generation + 1),
                        current.Id,
                        ct);
                }
                catch (InvalidOperationException) { return new(null, RuntimeMutationFailure.ConfigurationInvalid); }
                db.RuntimeInstances.Add(entity);
                break;
            case RuntimeAction.Stop:
                if (current is null || !IsActive(current.State))
                    return new(null, RuntimeMutationFailure.InvalidState);
                current.ProcessingVersion = checked(current.ProcessingVersion + 1);
                entity = current;
                if (current.State == RuntimeState.Queued)
                {
                    current.State = RuntimeState.Stopped;
                    current.StoppedAt = command.Now;
                }
                else
                {
                    current.State = RuntimeState.Stopping;
                    await outbox.PublishAsync(new StopRuntime(entity.Id, entity.ProcessingVersion));
                }
                break;
            case RuntimeAction.Extend:
                if (current is null || current.State != RuntimeState.Running || current.ExpiresAt is null)
                    return new(null, RuntimeMutationFailure.InvalidState);
                var remaining = current.ExpiresAt.Value - command.Now;
                if (remaining <= TimeSpan.Zero || remaining >= TimeSpan.FromMinutes(10))
                    return new(null, RuntimeMutationFailure.InvalidState);
                current.ExpiresAt = command.Now.Add(command.Extension!.Value);
                current.ProcessingVersion = checked(current.ProcessingVersion + 1);
                entity = current;
                break;
            default:
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

    private async Task<RuntimeInstance> CreateAsync(
        RuntimeScope scope,
        RuntimeMutationCommand command,
        int generation,
        Guid? replaces,
        CancellationToken cancellationToken)
    {
        var template = templates.Get(scope.Mode, scope.ConfigurationJson)
            ?? throw new InvalidOperationException("The challenge does not define a runtime template.");
        if (scope.Mode == GameMode.Ctf
            && template.FlagSource == RuntimeFlagSource.PerTeam)
        {
            _ = await runtimeFlags.EnsureAsync(
                command.CompetitionId,
                command.CompetitionChallengeId,
                scope.TeamId,
                command.Now,
                cancellationToken);
        }
        return new RuntimeInstance
        {
            Id = Guid.CreateVersion7(command.Now),
            CompetitionId = command.CompetitionId,
            CompetitionChallengeId = command.CompetitionChallengeId,
            TeamId = scope.TeamId,
            Purpose = RuntimePurpose.Player,
            Generation = generation,
            RuntimeKind = template.RuntimeKind,
            RuntimeProvider = template.Provider,
            RunnerPool = template.RunnerPool,
            State = RuntimeState.Queued,
            ConfigurationRevision = scope.ConfigurationRevision,
            ReplacesRuntimeInstanceId = replaces,
            CreatedAt = command.Now,
            ExpiresAt = null
        };
    }

    private async Task<RuntimeScope?> ResolveScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct) =>
        await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId &&
                team.MemberIds.Contains(userId) &&
                !team.IsBanned &&
                team.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Join(
                db.CompetitionChallenges.AsNoTracking(),
                team => team.CompetitionId,
                challenge => challenge.CompetitionId,
                (team, challenge) => new { Team = team, Challenge = challenge })
            .Join(
                db.Competitions.AsNoTracking(),
                pair => pair.Team.CompetitionId,
                competition => competition.Id,
                (pair, competition) => new { pair.Team, pair.Challenge, Competition = competition })
            .Where(item => item.Challenge.Id == competitionChallengeId)
            .Select(item => new RuntimeScope(
                item.Team.Id,
                item.Competition.Mode,
                item.Competition.Status,
                item.Challenge.ConfigurationJson,
                item.Challenge.Revision))
            .SingleOrDefaultAsync(ct);

    private Task AcquireLockAsync(Guid teamId, Guid challengeId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({teamId.ToString() + ":" + challengeId.ToString()}, 0))",
            ct);

    private static bool IsActive(RuntimeState state) =>
        state is RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping;

    private static RuntimeInstanceView Map(RuntimeInstance instance) =>
        new(
            instance.Id, instance.CompetitionId, instance.CompetitionChallengeId, instance.TeamId,
            instance.Generation, instance.RuntimeKind, instance.RuntimeProvider, instance.RunnerPool,
            instance.State, instance.FailureCode, instance.ProcessingVersion, instance.Urls,
            instance.CreatedAt, instance.RunningAt, instance.ExpiresAt, instance.StoppedAt);

    private sealed record RuntimeScope(
        Guid TeamId,
        GameMode Mode,
        CompetitionStatus Status,
        string ConfigurationJson,
        int ConfigurationRevision);
}

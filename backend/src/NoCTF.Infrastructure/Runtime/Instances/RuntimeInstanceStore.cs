using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Registration;

namespace NoCTF.Infrastructure.Runtime.Instances;

public sealed class RuntimeInstanceStore(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    IRuntimePlacementPolicy placementPolicy,
    IPerTeamRuntimeFlagStore runtimeFlags,
    ITransactionalMessageOutbox outbox,
    TeamRuntimeQuota runtimeQuota,
    ICompetitionEventRecorder? eventRecorder = null) : IRuntimeInstanceStore
{
    public RuntimeInstanceStore(
        NoCtfDbContext db,
        IChallengeRuntimeTemplateCatalog templates,
        IRuntimePlacementPolicy placementPolicy,
        IPerTeamRuntimeFlagStore runtimeFlags,
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder? eventRecorder = null)
        : this(
            db,
            templates,
            placementPolicy,
            runtimeFlags,
            outbox,
            new TeamRuntimeQuota(new LocalCriticalSectionRegistry()),
            eventRecorder) { }

    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<RuntimeInstanceView?> FindPlayerRuntimeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(competitionId, competitionChallengeId, userId, ct);
        if (scope is null
            || !AllowsRuntimeActions(scope)
            || scope.Mode == GameMode.Koh)
            return null;
        var purpose = PurposeFor(scope);
        var includesLegacyAwdpPurpose = scope.Mode == GameMode.Awdp
            && purpose == RuntimePurpose.Player;
        return await db.RuntimeInstances.AsNoTracking()
            .Where(instance =>
                instance.CompetitionId == competitionId &&
                instance.CompetitionChallengeId == competitionChallengeId &&
                (instance.Purpose == purpose
                    || includesLegacyAwdpPurpose
                    && instance.Purpose == RuntimePurpose.AwdpAttack) &&
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
        await LockCompetitionAsync(command.CompetitionId, ct);
        var scope = await ResolveScopeAsync(
            command.CompetitionId,
            command.CompetitionChallengeId,
            command.UserId,
            ct);
        if (scope is null || !AllowsRuntimeActions(scope))
            return new(null, RuntimeMutationFailure.NotFound);
        if (scope.Mode == GameMode.Koh)
            return new(null, RuntimeMutationFailure.Unsupported);
        if (scope.Mode == GameMode.Awd && command.Action is RuntimeAction.Start or RuntimeAction.Stop or RuntimeAction.Extend)
            return new(null, RuntimeMutationFailure.Unsupported);
        var purpose = PurposeFor(scope);
        var includesLegacyAwdpPurpose = scope.Mode == GameMode.Awdp
            && purpose == RuntimePurpose.Player;

        await using var quotaLease = await runtimeQuota.AcquireLockAsync(
            db,
            command.CompetitionId,
            scope.TeamId,
            ct);
        var current = await db.RuntimeInstances
            .Where(instance =>
                instance.CompetitionChallengeId == command.CompetitionChallengeId &&
                (instance.Purpose == purpose
                    || includesLegacyAwdpPurpose
                    && instance.Purpose == RuntimePurpose.AwdpAttack) &&
                instance.TeamId == scope.TeamId)
            .OrderByDescending(instance => instance.Generation)
            .FirstOrDefaultAsync(ct);
        var latestGeneration = await db.RuntimeInstances.AsNoTracking()
            .Where(instance =>
                instance.CompetitionChallengeId == command.CompetitionChallengeId
                && instance.TeamId == scope.TeamId)
            .MaxAsync(instance => (int?)instance.Generation, ct) ?? 0;

        RuntimeInstance entity;
        switch (command.Action)
        {
            case RuntimeAction.Start:
                if (current is not null && IsActive(current.State))
                    return scope.Mode == GameMode.Awdp
                        ? new(Map(current))
                        : new(null, RuntimeMutationFailure.InvalidState);
                if (await db.RuntimeInstances.AnyAsync(instance =>
                        instance.CompetitionId == command.CompetitionId &&
                        instance.CompetitionChallengeId == command.CompetitionChallengeId &&
                        (instance.Purpose == purpose
                            || includesLegacyAwdpPurpose
                            && instance.Purpose == RuntimePurpose.AwdpAttack) &&
                        instance.TeamId == scope.TeamId &&
                        instance.State == RuntimeState.Stopping,
                        ct))
                    return new(null, RuntimeMutationFailure.InvalidState);
                if (!await runtimeQuota.CanCreateSlotAsync(
                        db,
                        command.CompetitionId,
                        scope.TeamId,
                        command.CompetitionChallengeId,
                        scope.MaxConcurrentRuntimeInstances,
                        ct))
                    return new(null, RuntimeMutationFailure.CapacityExceeded);
                var cleanupTarget = await db.RuntimeInstances
                    .Where(instance =>
                        instance.CompetitionId == command.CompetitionId &&
                        instance.CompetitionChallengeId == command.CompetitionChallengeId &&
                        (instance.Purpose == purpose
                            || includesLegacyAwdpPurpose
                            && instance.Purpose == RuntimePurpose.AwdpAttack) &&
                        instance.TeamId == scope.TeamId &&
                        instance.State == RuntimeState.Failed &&
                        instance.RunnerId != null &&
                        (instance.ProviderReceiptJson != null
                            || instance.FailureCode == RuntimeFailureCode.CleanupFailed))
                    .OrderByDescending(instance => instance.Generation)
                    .FirstOrDefaultAsync(ct);
                if (cleanupTarget is not null)
                {
                    cleanupTarget.State = RuntimeState.Stopping;
                    cleanupTarget.FailureCode = null;
                    cleanupTarget.RunnerAssignmentReleaseToken = null;
                    cleanupTarget.ProcessingVersion = checked(cleanupTarget.ProcessingVersion + 1);
                    await outbox.PublishAsync(new StopRuntime(
                        cleanupTarget.Id,
                        cleanupTarget.ProcessingVersion));
                    await events.RecordAsync(new(
                        cleanupTarget.CompetitionId,
                        CompetitionEventKind.RuntimeStateChanged,
                        CompetitionEventLevel.Warning,
                        CompetitionEventVisibility.Team,
                        command.Now,
                        ActorUserId: command.UserId,
                        TeamId: cleanupTarget.TeamId,
                        CompetitionChallengeId: cleanupTarget.CompetitionChallengeId,
                        RuntimeInstanceId: cleanupTarget.Id,
                        RuntimeState: cleanupTarget.State,
                        RuntimeGeneration: cleanupTarget.Generation), ct);
                }
                try
                {
                    entity = await CreateAsync(
                        scope,
                        command,
                        checked(latestGeneration + 1),
                        cleanupTarget?.Id,
                        ct);
                }
                catch (InvalidOperationException) { return new(null, RuntimeMutationFailure.ConfigurationInvalid); }
                db.RuntimeInstances.Add(entity);
                if (cleanupTarget is null)
                    await outbox.PublishAsync(new DispatchRuntime(entity.Id, entity.ProcessingVersion));
                break;
            case RuntimeAction.Reset:
                if (current is null
                    || !CanReset(current))
                    return new(null, RuntimeMutationFailure.InvalidState);
                if (!await runtimeQuota.CanCreateSlotAsync(
                        db,
                        command.CompetitionId,
                        scope.TeamId,
                        command.CompetitionChallengeId,
                        scope.MaxConcurrentRuntimeInstances,
                        ct))
                    return new(null, RuntimeMutationFailure.CapacityExceeded);
                current.State = RuntimeState.Stopping;
                current.RunnerAssignmentReleaseToken = null;
                current.ProcessingVersion = checked(current.ProcessingVersion + 1);
                if (scope.Mode == GameMode.Awdp)
                    await runtimeFlags.InvalidateGenerationAsync(current.Id, command.Now, ct);
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
                if (current.State == RuntimeState.Stopping)
                    return new(Map(current));
                current.ProcessingVersion = checked(current.ProcessingVersion + 1);
                entity = current;
                if (scope.Mode == GameMode.Awdp)
                    await runtimeFlags.InvalidateGenerationAsync(current.Id, command.Now, ct);
                if (current.State == RuntimeState.Queued)
                {
                    current.State = RuntimeState.Stopped;
                    current.StoppedAt = command.Now;
                }
                else
                {
                    current.State = RuntimeState.Stopping;
                    current.RunnerAssignmentReleaseToken = null;
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
            var eventKind = command.Action switch
            {
                RuntimeAction.Start => CompetitionEventKind.RuntimeCreated,
                RuntimeAction.Reset => CompetitionEventKind.RuntimeReset,
                RuntimeAction.Stop => CompetitionEventKind.RuntimeStateChanged,
                RuntimeAction.Extend => CompetitionEventKind.RuntimeExtended,
                _ => throw new InvalidOperationException("Unsupported runtime event action.")
            };
            await events.RecordAsync(new(
                entity.CompetitionId,
                eventKind,
                entity.State == RuntimeState.Failed
                    ? CompetitionEventLevel.Error
                    : CompetitionEventLevel.Information,
                CompetitionEventVisibility.Team,
                command.Now,
                ActorUserId: command.UserId,
                TeamId: entity.TeamId,
                CompetitionChallengeId: entity.CompetitionChallengeId,
                RuntimeInstanceId: entity.Id,
                RuntimeState: entity.State,
                RuntimeGeneration: entity.Generation), ct);
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
        var template = templates.Get(scope.Mode, scope.DefinitionJson)
            ?? throw new InvalidOperationException("The challenge does not define a runtime template.");
        if (PurposeFor(scope) == RuntimePurpose.Practice
            && template.RuntimeKind is not RuntimeKind.Container and not RuntimeKind.Compose)
        {
            throw new InvalidOperationException(
                "Practice mode supports only Container and Compose Runtime templates.");
        }
        var id = Guid.CreateVersion7(command.Now);
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
        if (scope.Mode == GameMode.Awdp)
        {
            _ = await runtimeFlags.EnsureGenerationAsync(
                command.CompetitionId,
                command.CompetitionChallengeId,
                scope.TeamId,
                id,
                command.Now,
                cancellationToken);
        }
        var placement = placementPolicy.Resolve(template.RuntimeKind);
        return new RuntimeInstance
        {
            Id = id,
            CompetitionId = command.CompetitionId,
            CompetitionChallengeId = command.CompetitionChallengeId,
            TeamId = scope.TeamId,
            Purpose = PurposeFor(scope),
            Generation = generation,
            RuntimeKind = template.RuntimeKind,
            RuntimeProvider = placement.Provider,
            RunnerPool = placement.RunnerPool,
            State = RuntimeState.Queued,
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
                team.DeletedAt == null &&
                !team.IsBanned &&
                team.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Join(
                db.CompetitionChallenges.AsNoTracking()
                    .Where(challenge =>
                        challenge.IsPublished &&
                        challenge.DeletedAt == null),
                team => team.CompetitionId,
                challenge => challenge.CompetitionId,
                (team, challenge) => new { Team = team, Challenge = challenge })
            .Join(
                db.Competitions.AsNoTracking()
                    .Where(competition => competition.DeletedAt == null),
                pair => pair.Team.CompetitionId,
                competition => competition.Id,
                (pair, competition) => new { pair.Team, pair.Challenge, Competition = competition })
            .Join(
                db.Challenges.AsNoTracking()
                    .Where(template => template.DeletedAt == null),
                item => item.Challenge.ChallengeId,
                template => template.Id,
                (item, template) => new { item.Team, item.Challenge, item.Competition, Template = template })
            .Where(item => item.Challenge.Id == competitionChallengeId)
            .Select(item => new RuntimeScope(
                item.Team.Id,
                item.Competition.Mode,
                item.Competition.Status,
                item.Competition.PracticeModeEnabled,
                item.Competition.MaxConcurrentRuntimeInstancesPerTeam,
                item.Competition.ConfigurationJson,
                item.Challenge.RulesJson,
                item.Template.DefinitionJson))
            .SingleOrDefaultAsync(ct);

    private Task LockCompetitionAsync(Guid competitionId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM competitions WHERE id = {competitionId} FOR UPDATE",
            ct);

    private static bool IsActive(RuntimeState state) =>
        state is RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping;

    private static bool AllowsRuntimeActions(RuntimeScope scope) =>
        scope.Status == CompetitionStatus.Running
            && (scope.Mode != GameMode.Awdp || IsValidAwdpConfiguration(scope))
        || scope is
        {
            Mode: GameMode.Ctf,
            Status: CompetitionStatus.Finished,
            PracticeModeEnabled: true
        };

    private static RuntimePurpose PurposeFor(RuntimeScope scope) =>
        scope.Status == CompetitionStatus.Finished
            ? RuntimePurpose.Practice
            : RuntimePurpose.Player;

    private static bool IsValidAwdpConfiguration(RuntimeScope scope)
    {
        try
        {
            _ = AwdpConfigurationParser.ParseCompetition(scope.CompetitionConfigurationJson);
            return true;
        }
        catch (GameModeConfigurationException)
        {
            return false;
        }
    }

    private static bool CanReset(RuntimeInstance instance) =>
        instance.State is RuntimeState.Provisioning or RuntimeState.Running
        || instance is
        {
            State: RuntimeState.Queued,
            ReplacesRuntimeInstanceId: null
        };

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
        bool PracticeModeEnabled,
        int MaxConcurrentRuntimeInstances,
        string CompetitionConfigurationJson,
        string RulesJson,
        string DefinitionJson);
}

using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;

namespace NoCTF.Infrastructure.Runtime.Administration;

public sealed class AdminRuntimeStore(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    IRuntimePlacementPolicy placementPolicy,
    IPerTeamRuntimeFlagStore runtimeFlags,
    ITransactionalMessageOutbox outbox,
    TeamRuntimeQuota runtimeQuota,
    SharedRuntimeCriticalSection sharedRuntimeCriticalSection,
    ICompetitionEventRecorder? eventRecorder = null) : IAdminRuntimeStore
{
    public AdminRuntimeStore(
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
            new TeamRuntimeQuota(new AsyncKeyedLock.AsyncKeyedLocker<string>()),
            new SharedRuntimeCriticalSection(new AsyncKeyedLock.AsyncKeyedLocker<string>()),
            eventRecorder)
    { }

    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

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
        if (filter.HostPort is int hostPort)
            query = query.Where(item => item.PublishedPorts.Any(port => port.HostPort == hostPort));
        if (beforeCreatedAt is DateTimeOffset createdAt && beforeId is Guid id)
            query = query.Where(item =>
                item.CreatedAt < createdAt ||
                item.CreatedAt == createdAt && item.Id.CompareTo(id) < 0);
        var items = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(limit)
            .Select(item => new RuntimeInstanceView(
                item.Id, item.CompetitionId, item.CompetitionChallengeId, item.TeamId,
                item.Purpose, item.Generation, item.RuntimeKind, item.RuntimeProvider, item.RunnerPool,
                item.State, item.FailureCode, item.Urls,
                item.CreatedAt, item.RunningAt, item.ExpiresAt, item.StoppedAt,
                item.RunnerId, item.ProviderReceiptJson, item.ControlCheckUrl,
                item.PublishedPorts
                    .OrderBy(port => port.ServiceName)
                    .ThenBy(port => port.ContainerPort)
                    .Select(port => new RuntimePublishedPortView(
                        port.ServiceName,
                        port.ContainerPort,
                        port.HostPort,
                        port.AllocatedAt))
                    .ToArray(),
                db.CompetitionEvents
                    .Where(eventItem =>
                        eventItem.CompetitionId == item.CompetitionId
                        && eventItem.Kind == CompetitionEventKind.RuntimeStateChanged
                        && eventItem.SubjectType == EntityReferenceKind.RuntimeInstance
                        && eventItem.SubjectId == item.Id)
                    .OrderByDescending(eventItem => eventItem.OccurredAt)
                    .ThenByDescending(eventItem => eventItem.Id)
                    .Select(eventItem => (DateTimeOffset?)eventItem.OccurredAt)
                    .FirstOrDefault()))
            .ToListAsync(ct);
        return await AddTeamAttributionAsync(items, ct);
    }

    public async Task<RuntimeInstanceView?> FindAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        CancellationToken ct)
    {
        var item = await db.RuntimeInstances.AsNoTracking()
            .Where(item => item.Id == runtimeInstanceId && item.CompetitionId == competitionId)
            .Select(item => new RuntimeInstanceView(
                item.Id, item.CompetitionId, item.CompetitionChallengeId, item.TeamId,
                item.Purpose, item.Generation, item.RuntimeKind, item.RuntimeProvider, item.RunnerPool,
                item.State, item.FailureCode, item.Urls,
                item.CreatedAt, item.RunningAt, item.ExpiresAt, item.StoppedAt,
                item.RunnerId, item.ProviderReceiptJson, item.ControlCheckUrl,
                item.PublishedPorts
                    .OrderBy(port => port.ServiceName)
                    .ThenBy(port => port.ContainerPort)
                    .Select(port => new RuntimePublishedPortView(
                        port.ServiceName,
                        port.ContainerPort,
                        port.HostPort,
                        port.AllocatedAt))
                    .ToArray(),
                db.CompetitionEvents
                    .Where(eventItem =>
                        eventItem.CompetitionId == item.CompetitionId
                        && eventItem.Kind == CompetitionEventKind.RuntimeStateChanged
                        && eventItem.SubjectType == EntityReferenceKind.RuntimeInstance
                        && eventItem.SubjectId == item.Id)
                    .OrderByDescending(eventItem => eventItem.OccurredAt)
                    .ThenByDescending(eventItem => eventItem.Id)
                    .Select(eventItem => (DateTimeOffset?)eventItem.OccurredAt)
                    .FirstOrDefault()))
            .SingleOrDefaultAsync(ct);
        if (item is null)
            return null;
        return (await AddTeamAttributionAsync([item], ct))[0];
    }

    public async Task<RuntimeMutationResult> ForceTerminateAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        Guid actorUserId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var scope = await db.RuntimeInstances.AsNoTracking()
            .Where(instance =>
                instance.Id == runtimeInstanceId
                && instance.CompetitionId == competitionId)
            .Select(instance => new
            {
                instance.TeamId,
                instance.CompetitionChallengeId,
                StateChangedAt = db.CompetitionEvents
                    .Where(eventItem =>
                        eventItem.CompetitionId == competitionId
                        && eventItem.Kind == CompetitionEventKind.RuntimeStateChanged
                        && eventItem.SubjectType == EntityReferenceKind.RuntimeInstance
                        && eventItem.SubjectId == runtimeInstanceId)
                    .OrderByDescending(eventItem => eventItem.OccurredAt)
                    .ThenByDescending(eventItem => eventItem.Id)
                    .Select(eventItem => (DateTimeOffset?)eventItem.OccurredAt)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(ct);
        if (scope is null)
            return new(null, RuntimeMutationFailure.NotFound);

        using var criticalSection = scope.TeamId is Guid teamId
            ? await runtimeQuota.AcquireLockAsync(db, competitionId, teamId, ct)
            : await sharedRuntimeCriticalSection.AcquireAsync(
                db,
                scope.CompetitionChallengeId,
                ct);
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(candidate =>
            candidate.Id == runtimeInstanceId
            && candidate.CompetitionId == competitionId,
            ct);
        if (instance is null)
            return new(null, RuntimeMutationFailure.NotFound);
        var current = Map(instance, scope.StateChangedAt);
        if (!RuntimeForceTerminationPolicy.CanForceTerminate(current, now))
            return new(null, RuntimeMutationFailure.NotStuck);

        var runnerId = instance.RunnerId
            ?? throw new InvalidOperationException(
                "Force termination requires an owning Runner assignment.");
        instance.State = RuntimeState.Stopping;
        instance.FailureCode = null;
        instance.RunnerAssignmentReleaseToken = null;
        await outbox.PublishToRunnerNodeAsync(new ForceTerminateRuntime(
            instance.Id,
            instance.Generation,
            instance.RuntimeProvider,
            instance.RunnerPool,
            runnerId,
            actorUserId,
            reason,
            now));
        await events.RecordAsync(new(
            instance.CompetitionId,
            CompetitionEventKind.RuntimeForceTerminationRequested,
            CompetitionEventLevel.Warning,
            CompetitionEventVisibility.Staff,
            now,
            ActorUserId: actorUserId,
            TeamId: instance.TeamId,
            CompetitionChallengeId: instance.CompetitionChallengeId,
            RuntimeInstanceId: instance.Id,
            RuntimeState: instance.State,
            RuntimeCleanupResult: RuntimeCleanupResult.Pending,
            RuntimeGeneration: instance.Generation,
            Reason: reason), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(Map(instance, now));
    }

    public async Task<RuntimeMutationResult> TerminateAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var scope = await db.RuntimeInstances.AsNoTracking()
            .Where(instance =>
                instance.Id == runtimeInstanceId &&
                instance.CompetitionId == competitionId)
            .Select(instance => new
            {
                instance.TeamId,
                instance.CompetitionChallengeId
            })
            .SingleOrDefaultAsync(ct);
        if (scope is null)
            return new(null, RuntimeMutationFailure.NotFound);

        using var criticalSection = scope.TeamId is Guid teamId
            ? await runtimeQuota.AcquireLockAsync(db, competitionId, teamId, ct)
            : await sharedRuntimeCriticalSection.AcquireAsync(
                db,
                scope.CompetitionChallengeId,
                ct);
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(candidate =>
            candidate.Id == runtimeInstanceId &&
            candidate.CompetitionId == competitionId,
            ct);
        if (instance is null)
            return new(null, RuntimeMutationFailure.NotFound);
        if (instance.State == RuntimeState.Stopping)
            return new(Map(instance));
        if (instance.State == RuntimeState.Stopped ||
            instance is { State: RuntimeState.Failed, ProviderReceiptJson: null })
        {
            return new(null, RuntimeMutationFailure.InvalidState);
        }

        instance.RunnerAssignmentReleaseToken = null;
        if (instance.State == RuntimeState.Queued)
        {
            instance.State = RuntimeState.Stopped;
            instance.StoppedAt = now;
        }
        else
        {
            instance.State = RuntimeState.Stopping;
            instance.FailureCode = null;
            await outbox.PublishAsync(new StopRuntime(instance.Id));
        }

        await events.RecordAsync(new(
                instance.CompetitionId,
                CompetitionEventKind.RuntimeStateChanged,
                CompetitionEventLevel.Warning,
                instance.TeamId is null
                    ? CompetitionEventVisibility.Public
                    : CompetitionEventVisibility.Team,
                now,
                ActorUserId: actorUserId,
                TeamId: instance.TeamId,
                CompetitionChallengeId: instance.CompetitionChallengeId,
                RuntimeInstanceId: instance.Id,
                RuntimeState: instance.State,
                RuntimeGeneration: instance.Generation), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(Map(instance));
    }

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
            .Join(
                db.Challenges.AsNoTracking(),
                scope => scope.Challenge.ChallengeId,
                template => template.Id,
                (scope, template) => new
                {
                    scope.Competition,
                    Instance = scope.Challenge,
                    Template = template
                })
            .SingleOrDefaultAsync(ct);
        if (scope is null || scope.Competition.Status != CompetitionStatus.Running)
            return new(null, RuntimeMutationFailure.NotFound);
        if ((teamId is null) != (scope.Competition.Mode == GameMode.Koh))
            return new(null, RuntimeMutationFailure.Unsupported);
        if (teamId is not null && scope.Competition.Mode is not (GameMode.Ctf or GameMode.Awd or GameMode.Awdp))
            return new(null, RuntimeMutationFailure.Unsupported);
        if (teamId is Guid requestedTeamId
            && !await db.Teams.AsNoTracking().AnyAsync(team =>
                team.Id == requestedTeamId
                && team.CompetitionId == competitionId
                && team.DeletedAt == null
                && !team.IsBanned
                && team.RegistrationStatus == NoCTF.Domain.Teams.TeamRegistrationStatus.Approved,
                ct))
        {
            return new(null, RuntimeMutationFailure.NotFound);
        }

        var purpose = scope.Competition.Mode == GameMode.Awdp
            ? RuntimePurpose.AwdpAttack
            : RuntimePurpose.Player;
        var includesLegacyAwdpPurpose = purpose == RuntimePurpose.AwdpAttack;

        using var criticalSection = teamId is Guid lockedTeamId
            ? await runtimeQuota.AcquireLockAsync(
                db,
                competitionId,
                lockedTeamId,
                ct)
            : await sharedRuntimeCriticalSection.AcquireAsync(
                db,
                competitionChallengeId,
                ct);
        var current = await db.RuntimeInstances
            .Where(item =>
                item.CompetitionChallengeId == competitionChallengeId &&
                item.TeamId == teamId &&
                (item.Purpose == purpose
                    || includesLegacyAwdpPurpose
                    && item.Purpose == RuntimePurpose.Player))
            .OrderByDescending(item => item.Generation)
            .FirstOrDefaultAsync(ct);
        RuntimeInstance entity;
        if (action is RuntimeAction.Start or RuntimeAction.Reset)
        {
            if (action == RuntimeAction.Start && current is not null && IsActive(current.State))
                return new(null, RuntimeMutationFailure.InvalidState);
            if (action == RuntimeAction.Start
                && await db.RuntimeInstances.AnyAsync(item =>
                    item.CompetitionId == competitionId &&
                    item.CompetitionChallengeId == competitionChallengeId &&
                    item.TeamId == teamId &&
                    (item.Purpose == purpose
                        || includesLegacyAwdpPurpose
                        && item.Purpose == RuntimePurpose.Player) &&
                    item.State == RuntimeState.Stopping,
                    ct))
                return new(null, RuntimeMutationFailure.InvalidState);
            if (action == RuntimeAction.Reset
                && (current is null
                    || !CanReset(current)))
                return new(null, RuntimeMutationFailure.InvalidState);
            if (teamId is Guid quotaTeamId
                && !await runtimeQuota.CanCreateSlotAsync(
                    db,
                    competitionId,
                    quotaTeamId,
                    competitionChallengeId,
                    scope.Competition.MaxConcurrentRuntimeInstancesPerTeam,
                    ct))
                return new(null, RuntimeMutationFailure.CapacityExceeded);
            var cleanupTarget = action == RuntimeAction.Reset
                ? current
                : await db.RuntimeInstances
                    .Where(item =>
                        item.CompetitionId == competitionId &&
                        item.CompetitionChallengeId == competitionChallengeId &&
                        item.TeamId == teamId &&
                        (item.Purpose == purpose
                            || includesLegacyAwdpPurpose
                            && item.Purpose == RuntimePurpose.Player) &&
                        item.State == RuntimeState.Failed &&
                        item.RunnerId != null &&
                        (item.ProviderReceiptJson != null
                            || item.FailureCode == RuntimeFailureCode.CleanupFailed))
                    .OrderByDescending(item => item.Generation)
                    .FirstOrDefaultAsync(ct);
            if (cleanupTarget is not null)
            {
                if (scope.Competition.Mode == GameMode.Awdp)
                    await runtimeFlags.InvalidateGenerationAsync(cleanupTarget.Id, now, ct);
                cleanupTarget.State = RuntimeState.Stopping;
                cleanupTarget.FailureCode = null;
                cleanupTarget.RunnerAssignmentReleaseToken = null;
                await outbox.PublishAsync(new StopRuntime(cleanupTarget.Id));
                await events.RecordAsync(new(
                    cleanupTarget.CompetitionId,
                    CompetitionEventKind.RuntimeStateChanged,
                    CompetitionEventLevel.Warning,
                    cleanupTarget.TeamId is null
                        ? CompetitionEventVisibility.Public
                        : CompetitionEventVisibility.Team,
                    now,
                    TeamId: cleanupTarget.TeamId,
                    CompetitionChallengeId: cleanupTarget.CompetitionChallengeId,
                    RuntimeInstanceId: cleanupTarget.Id,
                    RuntimeState: cleanupTarget.State,
                    RuntimeGeneration: cleanupTarget.Generation), ct);
            }
            var template = templates.Get(scope.Competition.Mode, scope.Template.DefinitionJson);
            if (template is null)
                return new(null, RuntimeMutationFailure.ConfigurationInvalid);
            if (scope.Competition.Mode == GameMode.Ctf
                && template.FlagSource == RuntimeFlagSource.PerTeam
                && teamId is Guid runtimeTeamId)
            {
                try
                {
                    _ = await runtimeFlags.EnsureAsync(
                        competitionId,
                        competitionChallengeId,
                        runtimeTeamId,
                        now,
                        ct);
                }
                catch (InvalidOperationException)
                {
                    return new(null, RuntimeMutationFailure.ConfigurationInvalid);
                }
            }
            var placement = placementPolicy.Resolve(template.RuntimeKind);
            var runtimeInstanceId = Guid.CreateVersion7(now);
            if (scope.Competition.Mode == GameMode.Awdp
                && teamId is Guid generationTeamId)
            {
                try
                {
                    _ = await runtimeFlags.EnsureGenerationAsync(
                        competitionId,
                        competitionChallengeId,
                        generationTeamId,
                        runtimeInstanceId,
                        now,
                        ct);
                }
                catch (InvalidOperationException)
                {
                    return new(null, RuntimeMutationFailure.ConfigurationInvalid);
                }
            }
            entity = new RuntimeInstance
            {
                Id = runtimeInstanceId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = teamId,
                Purpose = purpose,
                Generation = checked((current?.Generation ?? 0) + 1),
                RuntimeKind = template.RuntimeKind,
                RuntimeProvider = placement.Provider,
                RunnerPool = placement.RunnerPool,
                State = RuntimeState.Queued,
                ReplacesRuntimeInstanceId = cleanupTarget?.Id,
                CreatedAt = now,
                ExpiresAt = null
            };
            db.RuntimeInstances.Add(entity);
            if (cleanupTarget is null)
                await outbox.PublishAsync(new DispatchRuntime(entity.Id));
        }
        else if (action == RuntimeAction.Stop)
        {
            if (current is null || !IsActive(current.State))
                return new(null, RuntimeMutationFailure.InvalidState);
            if (current.State == RuntimeState.Stopping)
                return new(Map(current));
            entity = current;
            if (scope.Competition.Mode == GameMode.Awdp)
                await runtimeFlags.InvalidateGenerationAsync(current.Id, now, ct);
            if (current.State == RuntimeState.Queued)
            {
                current.State = RuntimeState.Stopped;
                current.StoppedAt = now;
            }
            else
            {
                current.State = RuntimeState.Stopping;
                current.RunnerAssignmentReleaseToken = null;
                await outbox.PublishAsync(new StopRuntime(entity.Id));
            }
        }
        else if (action == RuntimeAction.Extend)
        {
            if (teamId is null || current is null || current.State != RuntimeState.Running ||
                current.ExpiresAt is null || extension is null || extension <= TimeSpan.Zero)
                return new(null, RuntimeMutationFailure.InvalidState);
            var remaining = current.ExpiresAt.Value - now;
            if (remaining <= TimeSpan.Zero || remaining >= TimeSpan.FromMinutes(10))
                return new(null, RuntimeMutationFailure.InvalidState);
            current.ExpiresAt = now.Add(extension.Value);
            entity = current;
        }
        else
        {
            return new(null, RuntimeMutationFailure.Unsupported);
        }
        try
        {
            var eventKind = action switch
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
                entity.TeamId is null
                    ? CompetitionEventVisibility.Public
                    : CompetitionEventVisibility.Team,
                now,
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

    private static bool IsActive(RuntimeState state) =>
        state is RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping;

    private static bool CanReset(RuntimeInstance instance) =>
        instance.State is RuntimeState.Provisioning or RuntimeState.Running
        || instance is
        {
            State: RuntimeState.Queued,
            ReplacesRuntimeInstanceId: null
        };

    private static RuntimeInstanceView Map(
        RuntimeInstance item,
        DateTimeOffset? stateChangedAt = null) =>
        new(
            item.Id, item.CompetitionId, item.CompetitionChallengeId, item.TeamId,
            item.Purpose, item.Generation, item.RuntimeKind, item.RuntimeProvider, item.RunnerPool,
            item.State, item.FailureCode, item.Urls,
            item.CreatedAt, item.RunningAt, item.ExpiresAt, item.StoppedAt,
            item.RunnerId, item.ProviderReceiptJson, item.ControlCheckUrl,
            item.PublishedPorts
                .OrderBy(port => port.ServiceName, StringComparer.Ordinal)
                .ThenBy(port => port.ContainerPort)
                .Select(port => new RuntimePublishedPortView(
                    port.ServiceName,
                    port.ContainerPort,
                    port.HostPort,
                    port.AllocatedAt))
                .ToArray(),
            stateChangedAt);

    private async Task<IReadOnlyList<RuntimeInstanceView>> AddTeamAttributionAsync(
        IReadOnlyList<RuntimeInstanceView> runtimes,
        CancellationToken ct)
    {
        if (runtimes.Count == 0)
            return runtimes;

        var runtimeIds = runtimes.Select(runtime => runtime.Id).ToArray();
        var sources = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtimeIds.Contains(runtime.Id))
            .Select(runtime => new
            {
                runtime.Id,
                SourceTeamId = runtime.TeamId
                    ?? db.GameplayFacts.AsNoTracking()
                        .Where(fact => fact.Id == runtime.GameplayFactId)
                        .Select(fact => fact.TeamId)
                        .FirstOrDefault()
                    ?? db.PatchUploads.AsNoTracking()
                        .Where(upload => upload.RuntimeInstanceId == runtime.Id)
                        .OrderBy(upload => upload.UploadedAt)
                        .ThenBy(upload => upload.Id)
                        .Select(upload => (Guid?)upload.TeamId)
                        .FirstOrDefault()
            })
            .ToDictionaryAsync(item => item.Id, item => item.SourceTeamId, ct);
        var sourceTeamIds = sources.Values
            .OfType<Guid>()
            .Distinct()
            .ToArray();
        var teamNames = await db.Teams.IgnoreQueryFilters().AsNoTracking()
            .Where(team => sourceTeamIds.Contains(team.Id))
            .ToDictionaryAsync(team => team.Id, team => team.Name, ct);

        return runtimes.Select(runtime =>
        {
            var sourceTeamId = sources.GetValueOrDefault(runtime.Id);
            return runtime with
            {
                SourceTeamId = sourceTeamId,
                SourceTeamName = sourceTeamId is Guid id
                    ? teamNames.GetValueOrDefault(id)
                    : null
            };
        }).ToArray();
    }
}

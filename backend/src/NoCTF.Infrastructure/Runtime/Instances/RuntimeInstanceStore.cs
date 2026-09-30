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
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Domain.Commands;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Competitions.Progression;

namespace NoCTF.Infrastructure.Runtime.Instances;

public sealed class RuntimeInstanceStore(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    IRuntimePlacementPolicy placementPolicy,
    IPerTeamRuntimeFlagStore runtimeFlags,
    IPostCommitMessagePublisher outbox,
    TeamRuntimeQuota runtimeQuota,
    ICompetitionEventRecorder? eventRecorder = null,
    IRequestReplay? replay = null) : IRuntimeInstanceStore
{
    public RuntimeInstanceStore(
        NoCtfDbContext db,
        IChallengeRuntimeTemplateCatalog templates,
        IRuntimePlacementPolicy placementPolicy,
        IPerTeamRuntimeFlagStore runtimeFlags,
        IPostCommitMessagePublisher outbox,
        ICompetitionEventRecorder? eventRecorder = null)
        : this(
            db,
            templates,
            placementPolicy,
            runtimeFlags,
            outbox,
            new TeamRuntimeQuota(new AsyncKeyedLock.AsyncKeyedLocker<string>()),
            eventRecorder)
    { }

    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<TeamRuntimeListPage?> ListTeamRuntimesAsync(
        Guid competitionId,
        Guid userId,
        int offset,
        int limit,
        bool desc,
        CancellationToken ct)
    {
        var teamId = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.Members.Any(member => member.UserId == userId)
                && team.DeletedAt == null
                && !team.IsBanned
                && team.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(ct);
        if (teamId is null) return null;

        var query = db.RuntimeInstances.IgnoreAutoIncludes().AsNoTracking()
            .Where(instance => instance.CompetitionId == competitionId
                && instance.TeamId == teamId
                && instance.ActiveSlot != null
                && (instance.Purpose == RuntimePurpose.Player
                    || instance.Purpose == RuntimePurpose.Practice
                    || instance.Purpose == RuntimePurpose.AwdpAttack)
                && (instance.State == RuntimeState.Queued
                    || instance.State == RuntimeState.Provisioning
                    || instance.State == RuntimeState.Running
                    || instance.State == RuntimeState.Stopping))
            .Join(db.CompetitionChallenges.IgnoreAutoIncludes().AsNoTracking(),
                instance => instance.CompetitionChallengeId,
                challenge => (Guid?)challenge.Id,
                (instance, challenge) => new { Instance = instance, Challenge = challenge })
            .Join(db.Challenges.IgnoreAutoIncludes().AsNoTracking(),
                pair => pair.Challenge.ChallengeId,
                template => template.Id,
                (pair, template) => new { pair.Instance, pair.Challenge, Template = template });
        var total = await query.CountAsync(ct);
        var ordered = desc
            ? query.OrderByDescending(item => item.Instance.CreatedAt)
                .ThenByDescending(item => item.Instance.Id)
            : query.OrderBy(item => item.Instance.CreatedAt)
                .ThenBy(item => item.Instance.Id);
        var items = await ordered.Skip(offset)
            .Take(limit)
            .Select(item => new TeamRuntimeItemView(
                item.Challenge.CustomTitle ?? item.Template.Title,
                new RuntimeInstanceView(
                    item.Instance.Id,
                    item.Instance.CompetitionId,
                    item.Instance.CompetitionChallengeId,
                    item.Instance.ChallengeId,
                    item.Instance.TeamId,
                    item.Instance.Purpose,
                    item.Instance.RuntimeKind,
                    item.Instance.RuntimeProvider,
                    item.Instance.State,
                    item.Instance.FailureCode,
                    item.Instance.CreatedAt,
                    item.Instance.RunningAt,
                    item.Instance.ExpiresAt,
                    item.Instance.StoppedAt,
                    item.Instance.RunnerId,
                    null,
                    null,
                    null,
                    null,
                    null,
                    item.Instance.AccessMode,
                    item.Instance.AccessEndpoints.OrderBy(endpoint => endpoint.BindingIndex)
                        .Select(endpoint => new RuntimeAccessEndpointView(
                            endpoint.BindingIndex,
                            endpoint.DirectAddress,
                            endpoint.TargetHost,
                            endpoint.TargetPort)).ToArray())))
            .ToListAsync(ct);
        return new TeamRuntimeListPage(items, total);
    }

    public async Task<RuntimeInstanceView?> FindPlayerRuntimeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct)
    {
        var scope = await ResolveReadScopeAsync(competitionId, competitionChallengeId, userId, ct);
        if (scope is null
            || !(scope.Status == CompetitionStatus.Running
                && (scope.Mode != GameMode.Awdp || scope.ValidAwdpConfiguration)
                || scope.Mode == GameMode.Ctf
                    && scope.Status == CompetitionStatus.Finished
                    && scope.PracticeModeEnabled)
            || scope.Mode == GameMode.Koh)
            return null;
        var purpose = scope.Status == CompetitionStatus.Finished
            ? RuntimePurpose.Practice
            : scope.Mode == GameMode.Awdp
                ? RuntimePurpose.AwdpAttack
                : RuntimePurpose.Player;
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Where(instance =>
                instance.CompetitionId == competitionId &&
                instance.CompetitionChallengeId == competitionChallengeId &&
                instance.Purpose == purpose &&
                instance.TeamId == scope.TeamId)
            .OrderByDescending(instance => instance.CreatedAt)
            .ThenByDescending(instance => instance.Id)
            .Select(instance => new RuntimeInstanceView(
                instance.Id, instance.CompetitionId, instance.CompetitionChallengeId, instance.ChallengeId, instance.TeamId,
                instance.Purpose, instance.RuntimeKind, instance.RuntimeProvider,
                instance.State, instance.FailureCode,
                instance.CreatedAt, instance.RunningAt, instance.ExpiresAt, instance.StoppedAt,
                instance.RunnerId, instance.PublishedPorts.Select(port => new RuntimePublishedPortView(
                    port.ServiceName, port.ContainerPort, port.HostPort)).ToArray(),
                null,
                null,
                null,
                null,
                instance.AccessMode,
                instance.AccessEndpoints.OrderBy(endpoint => endpoint.BindingIndex)
                    .Select(endpoint => new RuntimeAccessEndpointView(
                        endpoint.BindingIndex,
                        endpoint.DirectAddress,
                        endpoint.TargetHost,
                        endpoint.TargetPort)).ToArray(),
                instance.TrafficCaptureEnabled,
                instance.TrafficCaptureLimitBytes,
                instance.TrafficCaptureReservedBytes))
            .FirstOrDefaultAsync(ct);
        return runtime;
    }

    public async Task<RuntimeMutationResult> MutatePlayerRuntimeAsync(
        RuntimeMutationCommand command,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await MutatePlayerRuntimeOnceAsync(command, ct);
            }
            catch (Exception exception) when (attempt < 2
                && RelationalRetry.IsTransientConcurrency(exception))
            {
                outbox.DiscardPendingMessages();
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 31)), ct);
            }
        }
    }

    private async Task<RuntimeMutationResult> MutatePlayerRuntimeOnceAsync(
        RuntimeMutationCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            ct);
        var scope = await ResolveScopeAsync(
            command.CompetitionId,
            command.CompetitionChallengeId,
            command.UserId,
            ct);
        if (scope is null || !AllowsRuntimeActions(scope))
            return new(null, RuntimeMutationFailure.NotFound);
        if (scope.Mode == GameMode.Ctf
            && command.Action is RuntimeAction.Start or RuntimeAction.Reset or RuntimeAction.Extend
            && !await new ProgressionChallengeAccess(db).IsActiveAsync(
                command.CompetitionId, command.CompetitionChallengeId,
                scope.TeamId, ct))
            return new(null, RuntimeMutationFailure.NotFound);
        if (scope.Mode == GameMode.Koh)
            return new(null, RuntimeMutationFailure.Unsupported);
        if (scope.Mode == GameMode.Awd && command.Action is RuntimeAction.Start or RuntimeAction.Stop or RuntimeAction.Extend)
            return new(null, RuntimeMutationFailure.Unsupported);
        if (scope is { Mode: GameMode.Awdp, HasCorrectBreak: true }
            && command.Action is RuntimeAction.Start or RuntimeAction.Reset or RuntimeAction.Extend)
            return new(null, RuntimeMutationFailure.InvalidState);
        var purpose = PurposeFor(scope);

        using var quotaLease = await runtimeQuota.AcquireLockAsync(
            db,
            command.CompetitionId,
            scope.TeamId,
            ct);
        var prior = replay is null ? null : await replay.FindAsync<RuntimeCommandReceipt>(
            new(command.UserId, ReplayOperation.RuntimeMutation, command.CompetitionId, command.CompetitionChallengeId),
            new RuntimeReplayFingerprint(command.Action, command.Extension), ct);
        if (prior is not null)
        {
            var original = await db.RuntimeInstances.AsNoTracking().SingleOrDefaultAsync(item => item.Id == prior.RuntimeInstanceId, ct);
            return original is null ? new(null, RuntimeMutationFailure.NotFound) : new(Map(original));
        }
        var current = await db.RuntimeInstances
            .Where(instance =>
                instance.CompetitionChallengeId == command.CompetitionChallengeId &&
                instance.Purpose == purpose &&
                instance.TeamId == scope.TeamId)
            .OrderByDescending(instance => instance.CreatedAt)
            .ThenByDescending(instance => instance.Id)
            .FirstOrDefaultAsync(ct);

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
                        instance.Purpose == purpose &&
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
                        instance.Purpose == purpose &&
                        instance.TeamId == scope.TeamId &&
                        instance.State == RuntimeState.Failed &&
                        instance.RunnerId != null)
                    .OrderByDescending(instance => instance.CreatedAt)
                    .ThenByDescending(instance => instance.Id)
                    .FirstOrDefaultAsync(ct);
                if (cleanupTarget is not null)
                {
                    cleanupTarget.State = RuntimeState.Stopping;
                    cleanupTarget.FailureCode = null;
                    await outbox.PublishAsync(new StopRuntime(cleanupTarget.Id));
                    await events.RecordAsync(new(
                        cleanupTarget.CompetitionId!.Value,
                        CompetitionEventKind.RuntimeStateChanged,
                        CompetitionEventLevel.Warning,
                        CompetitionEventVisibility.Team,
                        command.Now,
                        ActorUserId: command.UserId,
                        TeamId: cleanupTarget.TeamId,
                        CompetitionChallengeId: cleanupTarget.CompetitionChallengeId,
                        RuntimeInstanceId: cleanupTarget.Id,
                        RuntimeState: cleanupTarget.State), ct);
                }
                try
                {
                    entity = await CreateAsync(scope, command, ct);
                }
                catch (InvalidOperationException) { return new(null, RuntimeMutationFailure.ConfigurationInvalid); }
                db.RuntimeInstances.Add(entity);
                await outbox.PublishAsync(new DispatchRuntime(entity.Id));
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
                if (scope.Mode == GameMode.Awdp)
                    await runtimeFlags.InvalidateRuntimeInstanceAsync(current.Id, command.Now, ct);
                await outbox.PublishAsync(new StopRuntime(current.Id));
                try
                {
                    entity = await CreateAsync(scope, command, ct);
                }
                catch (InvalidOperationException) { return new(null, RuntimeMutationFailure.ConfigurationInvalid); }
                db.RuntimeInstances.Add(entity);
                await outbox.PublishAsync(new DispatchRuntime(entity.Id));
                break;
            case RuntimeAction.Stop:
                if (current is null || !IsActive(current.State))
                    return new(null, RuntimeMutationFailure.InvalidState);
                if (current.State == RuntimeState.Stopping)
                    return new(Map(current));
                entity = current;
                if (scope.Mode == GameMode.Awdp)
                    await runtimeFlags.InvalidateRuntimeInstanceAsync(current.Id, command.Now, ct);
                if (current.State == RuntimeState.Queued)
                {
                    current.State = RuntimeState.Stopped;
                    current.StoppedAt = command.Now;
                }
                else
                {
                    current.State = RuntimeState.Stopping;
                    await outbox.PublishAsync(new StopRuntime(entity.Id));
                }
                break;
            case RuntimeAction.Extend:
                if (current is null || current.State != RuntimeState.Running || current.ExpiresAt is null)
                    return new(null, RuntimeMutationFailure.InvalidState);
                if (current.ExpiresAt > command.Now
                    && !RuntimeExtensionPolicy.IsWithinRenewalWindow(current.ExpiresAt, command.Now))
                    return new(null, RuntimeMutationFailure.ExtensionTooEarly);
                var extendedExpiry = RuntimeExtensionPolicy.CalculateExpiry(
                    current.ExpiresAt, command.Now, command.Extension);
                if (extendedExpiry is null)
                    return new(null, RuntimeMutationFailure.InvalidState);
                current.ExpiresAt = extendedExpiry;
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
                entity.CompetitionId!.Value,
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
                RuntimeState: entity.State), ct);
            replay?.Store(new RuntimeCommandReceipt(entity.Id));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushCommittedMessagesAsync();
            return new(Map(entity));
        }
        catch (DbUpdateException exception) when (!TransactionFailureClassifier.IsRetryable(exception))
        {
            await transaction.RollbackAsync(ct);
            outbox.DiscardPendingMessages();
            db.ChangeTracker.Clear();
            if (command.Action == RuntimeAction.Start && scope.Mode == GameMode.Awdp)
            {
                var winner = await db.RuntimeInstances.AsNoTracking()
                    .Where(instance =>
                        instance.CompetitionId == command.CompetitionId
                        && instance.CompetitionChallengeId == command.CompetitionChallengeId
                        && instance.Purpose == purpose
                        && instance.TeamId == scope.TeamId
                        && (instance.State == RuntimeState.Queued
                            || instance.State == RuntimeState.Provisioning
                            || instance.State == RuntimeState.Running
                            || instance.State == RuntimeState.Stopping))
                    .OrderByDescending(instance => instance.CreatedAt)
                    .ThenByDescending(instance => instance.Id)
                    .FirstOrDefaultAsync(ct);
                if (winner is not null)
                    return new(Map(winner));
            }
            return new(null, RuntimeMutationFailure.Conflict);
        }
    }

    private async Task<RuntimeInstance> CreateAsync(
        RuntimeScope scope,
        RuntimeMutationCommand command,
        CancellationToken cancellationToken)
    {
        var template = templates.Get(scope.Definition)
            ?? throw new InvalidOperationException("The challenge does not define a runtime template.");
        if (PurposeFor(scope) == RuntimePurpose.Practice
            && template.RuntimeKind is not RuntimeKind.Container)
        {
            throw new InvalidOperationException(
                "Practice mode supports only Container Runtime templates.");
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
            _ = await runtimeFlags.EnsureRuntimeInstanceAsync(
                command.CompetitionId,
                command.CompetitionChallengeId,
                scope.TeamId,
                id,
                command.Now,
                cancellationToken);
        }
        var placement = placementPolicy.Resolve(template.RuntimeKind);
        var runtime = RuntimeInstanceGeneratedCatalog.Create(PurposeFor(scope));
        runtime.Id = id;
        runtime.CompetitionId = command.CompetitionId;
        runtime.CompetitionChallengeId = command.CompetitionChallengeId;
        runtime.TeamId = scope.TeamId;
        runtime.RuntimeKind = template.RuntimeKind;
        runtime.RuntimeProvider = placement.Provider;
        runtime.AccessMode = scope.RuntimeAccessMode;
        runtime.TrafficCaptureEnabled = scope.TrafficCaptureEnabled;
        runtime.TrafficCaptureLimitBytes = scope.TrafficCaptureLimitBytes;
        runtime.State = RuntimeState.Queued;
        runtime.CreatedAt = command.Now;
        runtime.ExpiresAt = null;
        return runtime;
    }

    private async Task<RuntimeReadScope?> ResolveReadScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct) =>
        await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.Members.Any(member => member.UserId == userId)
                && team.DeletedAt == null
                && !team.IsBanned
                && team.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Join(db.CompetitionChallenges.AsNoTracking()
                    .Where(challenge => challenge.IsPublished && challenge.DeletedAt == null),
                team => team.CompetitionId,
                challenge => challenge.CompetitionId,
                (team, challenge) => new { Team = team, Challenge = challenge })
            .Join(db.Competitions.AsNoTracking().Where(competition => competition.DeletedAt == null),
                pair => pair.Team.CompetitionId,
                competition => competition.Id,
                (pair, competition) => new { pair.Team, pair.Challenge, Competition = competition })
            .Join(db.Challenges.AsNoTracking().Where(template => template.DeletedAt == null),
                item => item.Challenge.ChallengeId,
                template => template.Id,
                (item, template) => new { item.Team, item.Challenge, item.Competition, Template = template })
            .Where(item => item.Challenge.Id == competitionChallengeId)
            .Select(item => new RuntimeReadScope(
                item.Team.Id,
                item.Competition.Mode,
                item.Competition.Status,
                item.Competition.PracticeModeEnabled,
                db.Set<AwdpCompetitionModeConfiguration>().Any(configuration =>
                    configuration.CompetitionId == item.Competition.Id)
                && db.Set<AwdpCompetitionChallengeRules>().Any(rules =>
                    rules.CompetitionChallengeId == item.Challenge.Id)
                && db.Set<AwdpChallengeDefinition>().Any(definition =>
                    definition.ChallengeId == item.Template.Id)))
            .SingleOrDefaultAsync(ct);

    private async Task<RuntimeScope?> ResolveScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct) =>
        await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId &&
                team.Members.Any(member => member.UserId == userId) &&
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
                item.Competition.RuntimeAccessMode,
                item.Competition.TrafficCaptureEnabled,
                item.Competition.TrafficCaptureLimitBytes,
                item.Competition.ModeConfiguration!,
                item.Challenge.Rules!,
                item.Template.Definition,
                db.GameplayFacts.Any(fact =>
                    fact.CompetitionId == competitionId
                    && fact.CompetitionChallengeId == competitionChallengeId
                    && fact.TeamId == item.Team.Id
                    && fact.Kind == NoCTF.Domain.Gameplay.GameplayFactKind.BreakAttempt
                    && fact.State == NoCTF.Domain.Gameplay.GameplayFactState.Completed
                    && fact.Result == NoCTF.Domain.Gameplay.GameplayFactResult.Correct)))
            .SingleOrDefaultAsync(ct);

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
            : scope.Mode == GameMode.Awdp
                ? RuntimePurpose.AwdpAttack
                : RuntimePurpose.Player;

    private static bool IsValidAwdpConfiguration(RuntimeScope scope) =>
        scope.CompetitionConfiguration is AwdpCompetitionModeConfiguration
        && scope.Rules is AwdpCompetitionChallengeRules
        && scope.Definition is AwdpChallengeDefinition;

    private static bool CanReset(RuntimeInstance instance) =>
        instance.State is RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running;

    private static RuntimeInstanceView Map(RuntimeInstance instance) =>
        new(
            instance.Id, instance.CompetitionId, instance.CompetitionChallengeId, instance.ChallengeId, instance.TeamId,
            instance.Purpose, instance.RuntimeKind, instance.RuntimeProvider,
            instance.State, instance.FailureCode,
            instance.CreatedAt, instance.RunningAt, instance.ExpiresAt, instance.StoppedAt,
            AccessMode: instance.AccessMode,
            AccessEndpoints: instance.AccessEndpoints.OrderBy(endpoint => endpoint.BindingIndex)
                .Select(endpoint => new RuntimeAccessEndpointView(
                    endpoint.BindingIndex,
                    endpoint.DirectAddress,
                    endpoint.TargetHost,
                    endpoint.TargetPort)).ToArray(),
            TrafficCaptureEnabled: instance.TrafficCaptureEnabled,
            TrafficCaptureLimitBytes: instance.TrafficCaptureLimitBytes,
            TrafficCaptureReservedBytes: instance.TrafficCaptureReservedBytes);

    private sealed record RuntimeScope(
        Guid TeamId,
        GameMode Mode,
        CompetitionStatus Status,
        bool PracticeModeEnabled,
        int MaxConcurrentRuntimeInstances,
        RuntimeAccessMode RuntimeAccessMode,
        bool TrafficCaptureEnabled,
        long? TrafficCaptureLimitBytes,
        CompetitionModeConfiguration CompetitionConfiguration,
        CompetitionChallengeRules Rules,
        ChallengeDefinition? Definition,
        bool HasCorrectBreak);

    private sealed record RuntimeReadScope(
        Guid TeamId,
        GameMode Mode,
        CompetitionStatus Status,
        bool PracticeModeEnabled,
        bool ValidAwdpConfiguration);
}

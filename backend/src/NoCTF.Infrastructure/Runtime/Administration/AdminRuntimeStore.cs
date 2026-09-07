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
using NoCTF.Infrastructure.GameplayFacts.Awdp;
using NoCTF.Infrastructure.Challenges;
using NoCTF.Domain.Challenges;
using NoCTF.Application.Commands.Idempotency;

namespace NoCTF.Infrastructure.Runtime.Administration;

public sealed class AdminRuntimeStore(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    IRuntimePlacementPolicy placementPolicy,
    IPerTeamRuntimeFlagStore runtimeFlags,
    ITransactionalMessageOutbox outbox,
    TeamRuntimeQuota runtimeQuota,
    SharedRuntimeCriticalSection sharedRuntimeCriticalSection,
    ICompetitionEventRecorder? eventRecorder = null,
    IRequestReplay? replay = null) : IAdminRuntimeStore
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
                item.Id, item.CompetitionId, item.CompetitionChallengeId, item.ChallengeId, item.TeamId,
                item.Purpose, item.RuntimeKind, item.RuntimeProvider,
                item.State, item.FailureCode, item.Urls,
                item.CreatedAt, item.RunningAt, item.ExpiresAt, item.StoppedAt,
                item.RunnerId,
                item.PublishedPorts
                    .OrderBy(port => port.ServiceName)
                    .ThenBy(port => port.ContainerPort)
                    .Select(port => new RuntimePublishedPortView(
                        port.ServiceName,
                        port.ContainerPort,
                        port.HostPort))
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

    public async Task<IReadOnlyList<PlatformRuntimeInstanceView>> ListActiveContainersAsync(
        PlatformRuntimeFilter filter,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct)
    {
        var query = db.RuntimeInstances.AsNoTracking()
            .Where(item =>
                (item.RuntimeKind == RuntimeKind.Container
                    || item.RuntimeKind == RuntimeKind.Compose)
                && (item.State == RuntimeState.Queued
                    || item.State == RuntimeState.Provisioning
                    || item.State == RuntimeState.Running
                    || item.State == RuntimeState.Stopping));
        if (filter.Scope is PlatformRuntimeScope scope)
            query = query.Where(item => scope == PlatformRuntimeScope.ChallengeTest
                ? item.Purpose == RuntimePurpose.TemplateTest
                : item.Purpose != RuntimePurpose.TemplateTest);
        if (filter.State is RuntimeState state)
            query = query.Where(item => item.State == state);
        if (filter.RuntimeKind is RuntimeKind kind)
            query = query.Where(item => item.RuntimeKind == kind);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLowerInvariant();
            var competitions = db.Competitions.IgnoreQueryFilters()
                .Where(item => item.Title.ToLower().Contains(search)).Select(item => item.Id);
            var templates = db.Challenges.IgnoreQueryFilters()
                .Where(item => item.Title.ToLower().Contains(search)).Select(item => item.Id);
            var challenges = db.CompetitionChallenges.IgnoreQueryFilters()
                .Join(db.Challenges.IgnoreQueryFilters(), item => item.ChallengeId, item => item.Id,
                    (item, template) => new { item.Id, Title = item.CustomTitle ?? template.Title })
                .Where(item => item.Title.ToLower().Contains(search)).Select(item => item.Id);
            var teams = db.Teams.IgnoreQueryFilters()
                .Where(item => item.Name.ToLower().Contains(search)).Select(item => (Guid?)item.Id);
            // Historical Fix targets may lack TeamId. Match the same attribution used by the list.
            query = query.Where(item =>
                item.CompetitionId.HasValue && competitions.Contains(item.CompetitionId.Value)
                || item.ChallengeId.HasValue && templates.Contains(item.ChallengeId.Value)
                || item.CompetitionChallengeId.HasValue && challenges.Contains(item.CompetitionChallengeId.Value)
                || teams.Contains(item.TeamId
                    ?? db.GameplayFacts.Where(fact => fact.Id == item.GameplayFactId)
                        .Select(fact => fact.TeamId).FirstOrDefault()
                    ?? db.PatchUploads.Where(upload => upload.RuntimeInstanceId == item.Id)
                        .OrderBy(upload => upload.UploadedAt).ThenBy(upload => upload.Id)
                        .Select(upload => (Guid?)upload.TeamId).FirstOrDefault()));
        }
        if (beforeCreatedAt is DateTimeOffset createdAt && beforeId is Guid id)
        {
            query = query.Where(item =>
                item.CreatedAt < createdAt
                || item.CreatedAt == createdAt && item.Id.CompareTo(id) < 0);
        }

        var runtimes = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(limit)
            .Select(item => new RuntimeInstanceView(
                item.Id, item.CompetitionId, item.CompetitionChallengeId, item.ChallengeId, item.TeamId,
                item.Purpose, item.RuntimeKind, item.RuntimeProvider,
                item.State, item.FailureCode, item.Urls,
                item.CreatedAt, item.RunningAt, item.ExpiresAt, item.StoppedAt,
                item.RunnerId,
                item.PublishedPorts
                    .OrderBy(port => port.ServiceName)
                    .ThenBy(port => port.ContainerPort)
                    .Select(port => new RuntimePublishedPortView(
                        port.ServiceName,
                        port.ContainerPort,
                        port.HostPort))
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
        if (runtimes.Count == 0)
            return [];

        var attributed = await AddTeamAttributionAsync(runtimes, ct);
        var competitionIds = attributed
            .Select(runtime => runtime.CompetitionId)
            .OfType<Guid>()
            .Distinct()
            .ToArray();
        var competitionTitles = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(competition => competitionIds.Contains(competition.Id))
            .ToDictionaryAsync(
                competition => competition.Id,
                competition => competition.Title,
                ct);
        var competitionChallengeIds = attributed
            .Select(runtime => runtime.CompetitionChallengeId)
            .OfType<Guid>()
            .Distinct()
            .ToArray();
        var competitionChallengeTitles = await db.CompetitionChallenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(challenge => competitionChallengeIds.Contains(challenge.Id))
            .Join(
                db.Challenges.IgnoreQueryFilters().AsNoTracking(),
                challenge => challenge.ChallengeId,
                template => template.Id,
                (challenge, template) => new
                {
                    challenge.Id,
                    Title = challenge.CustomTitle ?? template.Title
                })
            .ToDictionaryAsync(item => item.Id, item => item.Title, ct);
        var templateChallengeIds = attributed
            .Select(runtime => runtime.ChallengeId)
            .OfType<Guid>()
            .Distinct()
            .ToArray();
        var templateChallengeTitles = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(challenge => templateChallengeIds.Contains(challenge.Id))
            .ToDictionaryAsync(challenge => challenge.Id, challenge => challenge.Title, ct);

        return attributed.Select(runtime => runtime.ChallengeId is Guid templateChallengeId
            ? new PlatformRuntimeInstanceView(
                runtime,
                PlatformRuntimeScope.ChallengeTest,
                null,
                templateChallengeTitles.GetValueOrDefault(templateChallengeId)
                    ?? templateChallengeId.ToString("D"))
            : new PlatformRuntimeInstanceView(
                runtime,
                PlatformRuntimeScope.Competition,
                runtime.CompetitionId is Guid competitionId
                    ? competitionTitles.GetValueOrDefault(competitionId)
                        ?? competitionId.ToString("D")
                    : null,
                runtime.CompetitionChallengeId is Guid competitionChallengeId
                    ? competitionChallengeTitles.GetValueOrDefault(competitionChallengeId)
                        ?? competitionChallengeId.ToString("D")
                    : runtime.Id.ToString("D")))
            .ToArray();
    }

    public async Task<RuntimeInstanceView?> FindAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        CancellationToken ct)
    {
        var item = await db.RuntimeInstances.AsNoTracking()
            .Where(item => item.Id == runtimeInstanceId && item.CompetitionId == competitionId)
            .Select(item => new RuntimeInstanceView(
                item.Id, item.CompetitionId, item.CompetitionChallengeId, item.ChallengeId, item.TeamId,
                item.Purpose, item.RuntimeKind, item.RuntimeProvider,
                item.State, item.FailureCode, item.Urls,
                item.CreatedAt, item.RunningAt, item.ExpiresAt, item.StoppedAt,
                item.RunnerId,
                item.PublishedPorts
                    .OrderBy(port => port.ServiceName)
                    .ThenBy(port => port.ContainerPort)
                    .Select(port => new RuntimePublishedPortView(
                        port.ServiceName,
                        port.ContainerPort,
                        port.HostPort))
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
                scope.CompetitionChallengeId!.Value,
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
        await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
            instance,
            db,
            outbox,
            events,
            now,
            AwdpFixRuntimeCleanupMode.CallerManaged,
            ct);
        await outbox.PublishToRunnerNodeAsync(new ForceTerminateRuntime(
            instance.Id,
            instance.RuntimeProvider,
            runnerId,
            actorUserId,
            reason,
            now));
        await events.RecordAsync(new(
            instance.CompetitionId!.Value,
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
            Reason: reason), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
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
                scope.CompetitionChallengeId!.Value,
                ct);
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(candidate =>
            candidate.Id == runtimeInstanceId &&
            candidate.CompetitionId == competitionId,
            ct);
        if (instance is null)
            return new(null, RuntimeMutationFailure.NotFound);
        if (instance.State == RuntimeState.Stopping)
        {
            await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
                instance,
                db,
                outbox,
                events,
                now,
                AwdpFixRuntimeCleanupMode.EnsureStop,
                ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushCommittedMessagesAsync();
            return new(Map(instance));
        }
        if (instance.State == RuntimeState.Stopped ||
            instance is { State: RuntimeState.Failed, ProviderReceiptJson: null })
        {
            return new(null, RuntimeMutationFailure.InvalidState);
        }

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

        await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
            instance,
            db,
            outbox,
            events,
            now,
            AwdpFixRuntimeCleanupMode.CallerManaged,
            ct);

        await events.RecordAsync(new(
                instance.CompetitionId!.Value,
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
                RuntimeState: instance.State), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return new(Map(instance));
    }

    public async Task<RuntimeMutationResult> TerminatePlatformAsync(
        Guid runtimeInstanceId,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var scope = await db.RuntimeInstances.AsNoTracking()
            .Where(instance => instance.Id == runtimeInstanceId)
            .Select(instance => new { instance.CompetitionId, instance.ChallengeId })
            .SingleOrDefaultAsync(ct);
        if (scope?.CompetitionId is Guid competitionId)
            return await TerminateAsync(competitionId, runtimeInstanceId, actorUserId, now, ct);
        return scope?.ChallengeId is Guid challengeId
            ? await TerminateTemplateTestAsync(runtimeInstanceId, challengeId, now, ct)
            : new(null, RuntimeMutationFailure.NotFound);
    }

    public async Task<RuntimeMutationResult> ForceTerminatePlatformAsync(
        Guid runtimeInstanceId,
        Guid actorUserId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var scope = await db.RuntimeInstances.AsNoTracking()
            .Where(instance => instance.Id == runtimeInstanceId)
            .Select(instance => new { instance.CompetitionId, instance.ChallengeId })
            .SingleOrDefaultAsync(ct);
        if (scope?.CompetitionId is Guid competitionId)
        {
            return await ForceTerminateAsync(
                competitionId,
                runtimeInstanceId,
                actorUserId,
                reason,
                now,
                ct);
        }
        return scope?.ChallengeId is Guid challengeId
            ? await ForceTerminateTemplateTestAsync(
                runtimeInstanceId,
                challengeId,
                actorUserId,
                reason,
                now,
                ct)
            : new(null, RuntimeMutationFailure.NotFound);
    }

    private async Task<RuntimeMutationResult> TerminateTemplateTestAsync(
        Guid runtimeInstanceId,
        Guid challengeId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await ChallengeTemplateCriticalSection.AcquireAsync(db, challengeId, ct) is null)
            return new(null, RuntimeMutationFailure.NotFound);
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(candidate =>
            candidate.Id == runtimeInstanceId
            && candidate.ChallengeId == challengeId
            && candidate.Purpose == RuntimePurpose.TemplateTest,
            ct);
        if (instance is null)
            return new(null, RuntimeMutationFailure.NotFound);
        if (instance.State == RuntimeState.Stopping)
        {
            await outbox.PublishAsync(new StopRuntime(instance.Id));
        }
        else if (instance.State == RuntimeState.Queued
            && instance.RunnerId is null
            && string.IsNullOrWhiteSpace(instance.ProviderReceiptJson))
        {
            instance.State = RuntimeState.Stopped;
            instance.StoppedAt = now;
        }
        else if (instance.State is RuntimeState.Provisioning or RuntimeState.Running
            || instance is { State: RuntimeState.Failed, ProviderReceiptJson: not null })
        {
            instance.State = RuntimeState.Stopping;
            instance.FailureCode = null;
            await outbox.PublishAsync(new StopRuntime(instance.Id));
        }
        else
        {
            return new(null, RuntimeMutationFailure.InvalidState);
        }

        await EndPendingTemplateTestFlagAsync(
            instance,
            RuntimeTestFlagState.Canceled,
            now,
            ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return new(Map(instance));
    }

    private async Task<RuntimeMutationResult> ForceTerminateTemplateTestAsync(
        Guid runtimeInstanceId,
        Guid challengeId,
        Guid actorUserId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await ChallengeTemplateCriticalSection.AcquireAsync(db, challengeId, ct) is null)
            return new(null, RuntimeMutationFailure.NotFound);
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(candidate =>
            candidate.Id == runtimeInstanceId
            && candidate.ChallengeId == challengeId
            && candidate.Purpose == RuntimePurpose.TemplateTest,
            ct);
        if (instance is null)
            return new(null, RuntimeMutationFailure.NotFound);
        var current = Map(instance);
        if (!RuntimeForceTerminationPolicy.CanForceTerminate(current, now))
            return new(null, RuntimeMutationFailure.NotStuck);
        var runnerId = instance.RunnerId
            ?? throw new InvalidOperationException(
                "Force termination requires an owning Runner assignment.");

        instance.State = RuntimeState.Stopping;
        instance.FailureCode = null;
        await EndPendingTemplateTestFlagAsync(
            instance,
            RuntimeTestFlagState.Failed,
            now,
            ct);
        await outbox.PublishToRunnerNodeAsync(new ForceTerminateRuntime(
            instance.Id,
            instance.RuntimeProvider,
            runnerId,
            actorUserId,
            reason,
            now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return new(Map(instance, now));
    }

    private async Task EndPendingTemplateTestFlagAsync(
        RuntimeInstance instance,
        RuntimeTestFlagState state,
        DateTimeOffset at,
        CancellationToken ct)
    {
        if (instance.TestFlagDelivery == RuntimeTestFlagDelivery.NotRequired)
            return;
        if (instance.TestFlagState == RuntimeTestFlagState.Pending)
            instance.TestFlagState = state;
        var flag = await db.ChallengeFlags.SingleOrDefaultAsync(candidate =>
            candidate.ChallengeId == instance.ChallengeId
            && candidate.SpecificationKind == SpecificationKind.RuntimeInstance
            && candidate.SpecificationId == instance.Id,
            ct);
        if (flag is not null)
            flag.ValidUntil ??= at;
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
        await NoCTF.Infrastructure.Competitions.Participation.CompetitionParticipationLock.AcquireAsync(db, competitionId, ct);
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
        if (replay?.ActorId is Guid actorId) {
            var prior = await replay.FindAsync<RuntimeCommandReceipt>(
                new(actorId, ReplayOperation.AdminRuntimeMutation, competitionId, competitionChallengeId),
                new { teamId, action, extension }, ct);
            if (prior is not null) {
                var original = await db.RuntimeInstances.AsNoTracking().SingleOrDefaultAsync(item => item.Id == prior.RuntimeInstanceId, ct);
                return original is null ? new(null, RuntimeMutationFailure.NotFound) : new(Map(original));
            }
        }
        var current = await db.RuntimeInstances
            .Where(item =>
                item.CompetitionChallengeId == competitionChallengeId &&
                item.TeamId == teamId &&
                item.Purpose == purpose)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
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
                    item.Purpose == purpose &&
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
                        item.Purpose == purpose &&
                        item.State == RuntimeState.Failed &&
                        item.RunnerId != null)
                    .OrderByDescending(item => item.CreatedAt)
                    .ThenByDescending(item => item.Id)
                    .FirstOrDefaultAsync(ct);
            if (cleanupTarget is not null)
            {
                if (scope.Competition.Mode == GameMode.Awdp)
                    await runtimeFlags.InvalidateRuntimeInstanceAsync(cleanupTarget.Id, now, ct);
                cleanupTarget.State = RuntimeState.Stopping;
                cleanupTarget.FailureCode = null;
                await outbox.PublishAsync(new StopRuntime(cleanupTarget.Id));
                await events.RecordAsync(new(
                    cleanupTarget.CompetitionId!.Value,
                    CompetitionEventKind.RuntimeStateChanged,
                    CompetitionEventLevel.Warning,
                    cleanupTarget.TeamId is null
                        ? CompetitionEventVisibility.Public
                        : CompetitionEventVisibility.Team,
                    now,
                    TeamId: cleanupTarget.TeamId,
                    CompetitionChallengeId: cleanupTarget.CompetitionChallengeId,
                    RuntimeInstanceId: cleanupTarget.Id,
                    RuntimeState: cleanupTarget.State), ct);
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
                    _ = await runtimeFlags.EnsureRuntimeInstanceAsync(
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
                RuntimeKind = template.RuntimeKind,
                RuntimeProvider = placement.Provider,
                State = RuntimeState.Queued,
                CreatedAt = now,
                ExpiresAt = null
            };
            db.RuntimeInstances.Add(entity);
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
                await runtimeFlags.InvalidateRuntimeInstanceAsync(current.Id, now, ct);
            if (current.State == RuntimeState.Queued)
            {
                current.State = RuntimeState.Stopped;
                current.StoppedAt = now;
            }
            else
            {
                current.State = RuntimeState.Stopping;
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
                entity.CompetitionId!.Value,
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
                RuntimeState: entity.State), ct);
            if (replay?.ActorId is not null) replay.Store(new RuntimeCommandReceipt(entity.Id));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushCommittedMessagesAsync();
            return new(Map(entity));
        }
        catch (DbUpdateException exception) when (!TransactionFailureClassifier.IsRetryable(exception))
        {
            return new(null, RuntimeMutationFailure.Conflict);
        }
    }

    private static bool IsActive(RuntimeState state) =>
        state is RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping;

    private static bool CanReset(RuntimeInstance instance) =>
        instance.State is RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running;

    private static RuntimeInstanceView Map(
        RuntimeInstance item,
        DateTimeOffset? stateChangedAt = null) =>
        new(
            item.Id, item.CompetitionId, item.CompetitionChallengeId, item.ChallengeId, item.TeamId,
            item.Purpose, item.RuntimeKind, item.RuntimeProvider,
            item.State, item.FailureCode, item.Urls,
            item.CreatedAt, item.RunningAt, item.ExpiresAt, item.StoppedAt,
            item.RunnerId,
            item.PublishedPorts
                .OrderBy(port => port.ServiceName, StringComparer.Ordinal)
                .ThenBy(port => port.ContainerPort)
                .Select(port => new RuntimePublishedPortView(
                    port.ServiceName,
                    port.ContainerPort,
                    port.HostPort))
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

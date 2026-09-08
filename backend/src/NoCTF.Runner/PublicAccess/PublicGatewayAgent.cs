using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Platform;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.PublicAccess;
using NoCTF.Runtime.Docker.PublicAccess;
using StackExchange.Redis;

namespace NoCTF.Runner.PublicAccess;

/// <summary>Optional bounded control plane; FRP sidecars alone carry player traffic.</summary>
public sealed class PublicGatewayAgent(IServiceScopeFactory scopes, DockerPublicGateway docker,
    PublicGatewayCapability capability, IPublicGatewayStatusStore statuses, IConnectionMultiplexer redis,
    TimeProvider clock, ILogger<PublicGatewayAgent> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, GatewayPublication> publications = new();
    private readonly SemaphoreSlim wake = new(0, 1);
    private volatile bool enabled;
    private readonly string ownerToken = Guid.NewGuid().ToString("N");
    private string LeaderKey => "noctf:public-gateway:owner:" + capability.ConnectorId;
    private bool ownsLease;
    private bool waitingForOwnership;
    private bool cleanupNeeded = true;
    public void Signal()
    {
        try { if (wake.CurrentCount == 0) wake.Release(); }
        catch (SemaphoreFullException) { /* Concurrent invalidations are coalesced. */ }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ChannelMessageQueue? events = null;
        try
        {
            try
            {
                events = await redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(RedisCompetitionEventRefreshPublisher.Channel));
                events.OnMessage(_ => { if (enabled) Signal(); });
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Gateway event subscription unavailable: {FailureType}.", exception.GetType().Name); }
            while (!stoppingToken.IsCancellationRequested)
            {
                while (wake.Wait(0)) { }
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                budget.CancelAfter(TimeSpan.FromSeconds(15));
                var retry = false;
                try { await ReconcileAsync(budget.Token); }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // No runtime or gameplay state is rewritten on gateway failure. Leases expire independently.
                    logger.LogWarning("Gateway reconciliation deferred after {FailureType}.", exception.GetType().Name);
                    retry = true;
                }
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                if (retry || waitingForOwnership || enabled || !publications.IsEmpty) wait.CancelAfter(TimeSpan.FromSeconds(3));
                try { await wake.WaitAsync(wait.Token); }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            }
        }
        finally
        {
            if (events is not null) await events.UnsubscribeAsync();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Parallel.ForEachAsync(publications.Values, new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (publication, _) =>
            {
                try { await docker.RevokeAsync(publication, cleanup.Token); }
                catch (Exception) { /* Independent ten-second leases are the shutdown fallback. */ }
            });
            try { await redis.GetDatabase().LockReleaseAsync(LeaderKey, ownerToken); }
            catch (Exception) { /* Ownership expires without renewal. */ }
        }
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        var coordination = redis.GetDatabase();
        var held = ownsLease && await coordination.LockExtendAsync(LeaderKey, ownerToken, TimeSpan.FromSeconds(9));
        if (!held) held = await coordination.LockTakeAsync(LeaderKey, ownerToken, TimeSpan.FromSeconds(9));
        waitingForOwnership = !held;
        if (!held) { ownsLease = false; cleanupNeeded = true; return; }
        ownsLease = true;
        if (cleanupNeeded)
        {
            await docker.RevokeStaleHelpersAsync(ct);
            publications.Clear();
            cleanupNeeded = false;
        }
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var policy = ReadPolicy(await db.PlatformSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct));
        enabled = policy.Enabled;
        var now = clock.GetUtcNow();
        var rows = !enabled ? [] : await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.RunnerId == capability.RunnerId && runtime.State == RuntimeState.Running
                && runtime.RuntimeProvider == RuntimeProvider.Docker && runtime.RuntimeKind == RuntimeKind.Container
                && runtime.ExpiresAt > now && runtime.TeamId != null
                && (runtime.Purpose == RuntimePurpose.Player || runtime.Purpose == RuntimePurpose.AwdpAttack || runtime.Purpose == RuntimePurpose.Practice)
                && db.Teams.Any(team => team.Id == runtime.TeamId && team.DeletedAt == null && !team.IsBanned
                    && team.RegistrationStatus == TeamRegistrationStatus.Approved))
            .Join(db.CompetitionChallenges.AsNoTracking().Where(challenge => challenge.IsPublished), runtime => runtime.CompetitionChallengeId,
                challenge => (Guid?)challenge.Id, (runtime, challenge) => new { Runtime = runtime, Challenge = challenge })
            .Join(db.Challenges.AsNoTracking(), item => item.Challenge.ChallengeId, template => template.Id,
                (item, template) => new { item.Runtime, item.Challenge, Template = template })
            .Join(db.Competitions.AsNoTracking(), item => item.Challenge.CompetitionId, competition => competition.Id,
                (item, competition) => new { item.Runtime, item.Template.DefinitionJson, competition.Mode, competition.Status, competition.PracticeModeEnabled })
            .Where(item => item.Mode == GameMode.Ctf && (item.Runtime.Purpose == RuntimePurpose.Player || item.Runtime.Purpose == RuntimePurpose.Practice)
                || item.Mode == GameMode.Awdp && (item.Runtime.Purpose == RuntimePurpose.AwdpAttack || item.Runtime.Purpose == RuntimePurpose.Practice))
            .Where(item => item.Runtime.Purpose == RuntimePurpose.Practice
                ? item.Status == CompetitionStatus.Finished && item.PracticeModeEnabled
                : item.Status == CompetitionStatus.Running || item.Status == CompetitionStatus.Paused)
            .OrderBy(item => item.Runtime.CreatedAt).ThenBy(item => item.Runtime.Id)
            .Take(capability.MaximumPorts + 1)
            .ToListAsync(ct);
        var allowed = new Dictionary<Guid, Candidate>();
        var states = new ConcurrentDictionary<Guid, PublicRuntimeStatus>();
        var remaining = Math.Min(policy.MaxPublishedPorts, capability.MaximumPorts);
        var policyValid = PublicGatewayPolicyRules.Validate(policy, capability).Count == 0;
        foreach (var row in rows)
        {
            try
            {
                var receipt = JsonSerializer.Deserialize<ContainerReceipt>(row.Runtime.ProviderReceiptJson ?? "null", JsonOptions);
                var template = new ChallengeRuntimeTemplateCatalog().Get(row.Mode, row.DefinitionJson);
                var bindings = template?.UrlBindings ?? [];
                var logicalPorts = bindings.Where(binding => binding.ServiceName is null
                    && binding.Exposure is RuntimeExposure.OwnerOnly or RuntimeExposure.Participants
                    && binding.ContainerPort != null && binding.UrlTemplate.Contains("{HOST}", StringComparison.Ordinal)
                    && binding.UrlTemplate.Contains("{PORT}", StringComparison.Ordinal))
                    .Select(binding => binding.ContainerPort!.Value).Distinct().ToHashSet();
                var ports = row.Runtime.PublishedPorts.Where(port => port.ServiceName is null && logicalPorts.Contains(port.ContainerPort))
                    .Select(port => new RuntimePublishedPortView(port.ServiceName, port.ContainerPort, port.HostPort)).ToArray();
                var failure = !policyValid ? PublicAccessFailure.GatewaySafetyCheckFailed
                    : receipt is null || ports.Length == 0 || ports.Select(port => port.ContainerPort).Distinct().Count() != ports.Length
                        ? PublicAccessFailure.RuntimeBindingUnavailable
                    : ports.Any(port => !capability.AllowsPort(port.HostPort)) ? PublicAccessFailure.PublicPortUnavailable
                    : ports.Length > remaining ? PublicAccessFailure.GatewayCapacityExceeded : (PublicAccessFailure?)null;
                if (failure is not null)
                {
                    states[row.Runtime.Id] = State(row.Runtime.Id, ports, PublicAccessState.Unavailable, failure);
                    continue;
                }
                remaining -= ports.Length;
                allowed[row.Runtime.Id] = new(row.Runtime.Id, receipt!.ResourceId, ports);
            }
            catch (Exception exception) when (exception is JsonException or GameModeConfigurationException)
            { states[row.Runtime.Id] = State(row.Runtime.Id, [], PublicAccessState.Unsupported, PublicAccessFailure.RuntimeBindingUnavailable); }
        }
        foreach (var pair in publications.Where(pair => !allowed.ContainsKey(pair.Key)).ToArray())
        {
            try
            {
                using var revoke = CancellationTokenSource.CreateLinkedTokenSource(ct);
                revoke.CancelAfter(TimeSpan.FromSeconds(1));
                await docker.RevokeAsync(pair.Value, revoke.Token);
                publications.TryRemove(pair.Key, out _);
                await statuses.RemoveAsync(pair.Key, ct);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Gateway revocation pending for {RuntimeId}: {FailureType}.", pair.Key, exception.GetType().Name);
                states[pair.Key] = State(pair.Key, pair.Value.Ports, PublicAccessState.Revoking, PublicAccessFailure.GatewayReconciliationPending);
            }
        }
        await Parallel.ForEachAsync(allowed.Values, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, async (candidate, token) =>
        {
            try
            {
                if (!publications.TryGetValue(candidate.RuntimeId, out var publication))
                {
                    publication = await docker.StartAsync(candidate.RuntimeId, candidate.TargetId, candidate.Ports, token);
                    publications[candidate.RuntimeId] = publication;
                }
                if (publication.TargetId != candidate.TargetId || !publication.Ports.SequenceEqual(candidate.Ports))
                    throw new InvalidOperationException("Publication binding changed.");
                await RenewEligibleAsync(publication, policy, token);
                var endpoints = await docker.ReadStatusAsync(publication, token);
                var status = new PublicRuntimeStatus(publication.RuntimeId, capability.ConnectorId, capability.RunnerId,
                    clock.GetUtcNow().AddSeconds(5), endpoints);
                states[publication.RuntimeId] = status;
                await statuses.SetAsync(status, token);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Gateway publication unavailable for {RuntimeId}: {FailureType}.", candidate.RuntimeId, exception.GetType().Name);
                states[candidate.RuntimeId] = State(candidate.RuntimeId, candidate.Ports, PublicAccessState.Unavailable, PublicAccessFailure.GatewayIdentityRejected);
                await statuses.SetAsync(states[candidate.RuntimeId], token);
                if (publications.TryRemove(candidate.RuntimeId, out var stale))
                {
                    try { await docker.RevokeAsync(stale, token); }
                    catch (Exception) { publications[candidate.RuntimeId] = stale; }
                }
            }
        });
        foreach (var status in states.Values) await statuses.SetAsync(status, ct);
        await statuses.SetConnectorAsync(new(capability.ConnectorId, policy.Fingerprint(), !enabled && publications.IsEmpty ? DateTimeOffset.MaxValue : clock.GetUtcNow().AddSeconds(10),
            states.Values.OrderBy(value => value.RuntimeId).ToArray(), policyValid ? null : PublicAccessFailure.GatewaySafetyCheckFailed), ct);
    }

    private async Task RenewEligibleAsync(GatewayPublication publication, PublicGatewayPolicy expected, CancellationToken ct)
    {
        if ((string?)await redis.GetDatabase().LockQueryAsync(LeaderKey) != ownerToken)
            throw new InvalidOperationException("Gateway ownership lease was lost.");
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PublicGatewayLeaseGuard>().RenewAsync(publication.RuntimeId,
            capability.RunnerId, expected, (expires, token) => docker.RenewAsync(publication, expires, token), ct);
    }

    private PublicRuntimeStatus State(Guid runtimeId, IReadOnlyList<RuntimePublishedPortView> ports, PublicAccessState state, PublicAccessFailure? failure) =>
        new(runtimeId, capability.ConnectorId, capability.RunnerId, clock.GetUtcNow().AddSeconds(5),
            ports.Select(port => new PublicEndpointStatus(port.ContainerPort, port.HostPort, state, failure)).ToArray(), failure);
    private static PublicGatewayPolicy ReadPolicy(PlatformSettings value) => new(value.PublicGatewayEnabled,
        value.PublicGatewayConnectorId, value.PublicGatewayOrigin, value.PublicGatewayDirectOrigins, value.PublicGatewayRuntimeHost,
        value.PublicGatewayDirectHostOverride, value.PublicGatewayMaxPorts);
    private sealed record Candidate(Guid RuntimeId, string TargetId, IReadOnlyList<RuntimePublishedPortView> Ports);
}

public sealed class ReconcilePublicGatewayHandler(PublicGatewayAgent? coordinator = null, IPublicGatewayPolicyStore? policies = null)
{
    public async Task Handle(ReconcilePublicGateway message, CancellationToken ct)
    {
        if (policies is not null) await policies.InvalidateAsync(ct);
        coordinator?.Signal();
    }
}

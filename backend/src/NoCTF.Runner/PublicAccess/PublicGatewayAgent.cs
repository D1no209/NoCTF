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

/// <summary>Optional bounded control plane; isolated gateway endpoints alone carry player traffic.</summary>
public sealed class PublicGatewayAgent(IServiceScopeFactory scopes, IPublicGatewayTransport docker,
    PublicGatewayCapability capability, IPublicGatewayStatusStore statuses, IConnectionMultiplexer redis,
    TimeProvider clock, ILogger<PublicGatewayAgent> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, GatewayPublication> publications = new();
    private readonly SemaphoreSlim wake = new(0, 1);
    private volatile bool enabled;
    private readonly string ownerToken = Guid.NewGuid().ToString("N");
    private string LeaderKey => "noctf:public-gateway:owner:" + capability.ConnectorId;
    private volatile bool ownsLease;
    private volatile bool waitingForOwnership;
    private volatile bool cleanupNeeded = true;
    private volatile bool reconciled;
    private readonly object ownershipSync = new();
    private CancellationTokenSource? ownershipCancellation;
    private PublicGatewayPolicy renewalPolicy = PublicGatewayPolicy.Disabled;
    public async Task CheckReadinessAsync(CancellationToken ct)
    {
        var ownership = OwnershipToken();
        if (ownership is null || !reconciled || cleanupNeeded || ExecuteTask is not { IsCompleted: false })
            throw new InvalidOperationException("Public gateway ownership or initialization is unavailable.");
        // Verify the actual Redis owner, without acquiring/renewing a lease or exposing its token.
        var owner = await redis.GetDatabase().LockQueryAsync(LeaderKey).WaitAsync(ct);
        if (owner != ownerToken || OwnershipToken() != ownership || !reconciled || cleanupNeeded
            || ExecuteTask is not { IsCompleted: false })
            throw new InvalidOperationException("Public gateway ownership is unavailable.");
    }
    public void Signal()
    {
        try { if (wake.CurrentCount == 0) wake.Release(); }
        catch (SemaphoreFullException) { /* Concurrent invalidations are coalesced. */ }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ChannelMessageQueue? events = null;
        using var loops = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var ownershipLoop = MaintainOwnershipAsync(loops.Token);
        var renewalLoop = RenewPublicationsAsync(loops.Token);
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
                var ownership = OwnershipToken();
                if (ownership is null) { await wake.WaitAsync(stoppingToken); continue; }
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, ownership.Value);
                budget.CancelAfter(TimeSpan.FromSeconds(15));
                var retry = false;
                try
                {
                    await ReconcileAsync(ownership.Value, budget.Token);
                    lock (ownershipSync)
                        reconciled = ownsLease && ownershipCancellation?.Token == ownership.Value;
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // No runtime or gameplay state is rewritten on gateway failure. Leases expire independently.
                    logger.LogWarning("Gateway reconciliation deferred after {FailureType}.", exception.GetType().Name);
                    reconciled = false;
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
            reconciled = false;
            await loops.CancelAsync();
            try { await Task.WhenAll(ownershipLoop, renewalLoop); }
            catch (OperationCanceledException) when (loops.IsCancellationRequested) { }
            if (events is not null) await events.UnsubscribeAsync();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            // Close the shared data path first; do not spend the entire shutdown budget canceling
            // individual forwards while leaving the shared client alive.
            try { await docker.StopAsync(cleanup.Token); }
            catch (Exception exception) { logger.LogWarning("Gateway transport shutdown deferred: {FailureType}.", exception.GetType().Name); }
            await Parallel.ForEachAsync(publications.Values, new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (publication, _) =>
            {
                try { await docker.RevokeAsync(publication, cleanup.Token); }
                catch (Exception) { /* Independent ten-second leases are the shutdown fallback. */ }
            });
            try { await redis.GetDatabase().LockReleaseAsync(LeaderKey, ownerToken); }
            catch (Exception) { /* Ownership expires without renewal. */ }
        }
    }

    private CancellationToken? OwnershipToken()
    {
        lock (ownershipSync) return ownsLease ? ownershipCancellation?.Token : null;
    }

    private void LoseOwnership()
    {
        CancellationTokenSource? previous;
        lock (ownershipSync)
        {
            ownsLease = false; cleanupNeeded = true; reconciled = false;
            previous = ownershipCancellation; ownershipCancellation = null;
        }
        previous?.Cancel(); previous?.Dispose();
        Signal();
    }

    private async Task MaintainOwnershipAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(TimeSpan.FromSeconds(1));
                try
                {
                    var coordination = redis.GetDatabase();
                    var held = ownsLease
                        ? await coordination.LockExtendAsync(LeaderKey, ownerToken, TimeSpan.FromSeconds(9)).WaitAsync(budget.Token)
                        : await coordination.LockTakeAsync(LeaderKey, ownerToken, TimeSpan.FromSeconds(9)).WaitAsync(budget.Token);
                    waitingForOwnership = !held;
                    if (!held) LoseOwnership();
                    else if (!ownsLease)
                    {
                        lock (ownershipSync)
                        {
                            ownershipCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            cleanupNeeded = true; ownsLease = true;
                        }
                        Signal();
                    }
                }
                catch (Exception exception) when (!ct.IsCancellationRequested)
                {
                    LoseOwnership();
                    logger.LogWarning("Gateway ownership unavailable: {FailureType}.", exception.GetType().Name);
                }
                await Task.Delay(TimeSpan.FromSeconds(2), clock, ct);
            }
        }
        finally { LoseOwnership(); }
    }

    private async Task RenewPublicationsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var ownership = OwnershipToken();
            if (docker.UsesIndependentPublicPorts && !cleanupNeeded && ownership is { } owned)
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, owned);
                budget.CancelAfter(TimeSpan.FromMilliseconds(1500));
                try
                {
                    await Parallel.ForEachAsync(publications.Values, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = budget.Token }, async (publication, token) =>
                    {
                        try { await RenewEligibleAsync(publication, Volatile.Read(ref renewalPolicy), token); }
                        catch (Exception exception) when (!token.IsCancellationRequested)
                        { logger.LogDebug("Gateway lease not renewed for {RuntimeId}: {FailureType}.", publication.RuntimeId, exception.GetType().Name); }
                    });
                }
                catch (OperationCanceledException) when (budget.IsCancellationRequested) { }
            }
            await Task.Delay(TimeSpan.FromSeconds(2), clock, ct);
        }
    }

    private async Task ReconcileAsync(CancellationToken ownership, CancellationToken ct)
    {
        if (cleanupNeeded)
        {
            await docker.RevokeStaleHelpersAsync(ct);
            publications.Clear();
            lock (ownershipSync)
            {
                if (!ownsLease || ownershipCancellation?.Token != ownership) throw new OperationCanceledException(ct);
                cleanupNeeded = false;
            }
        }
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var policy = ReadPolicy(await db.PlatformSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct));
        Volatile.Write(ref renewalPolicy, policy);
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
                    : !docker.UsesIndependentPublicPorts && ports.Any(port => !capability.AllowsPort(port.HostPort)) ? PublicAccessFailure.PublicPortUnavailable
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
                await statuses.SetAsync(State(pair.Key, pair.Value.Ports, PublicAccessState.Revoking, PublicAccessFailure.GatewayReconciliationPending), ct);
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
                    // Invalidate old Ready before creating a replacement; cache consumers must not
                    // mistake the former publication's short-lived status for the new endpoint.
                    await statuses.SetAsync(State(candidate.RuntimeId, candidate.Ports, PublicAccessState.Pending, PublicAccessFailure.GatewayReconciliationPending), token);
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
                logger.LogWarning("Gateway publication unavailable for {RuntimeId}: {FailureType}, {FailureCode}.", candidate.RuntimeId,
                    exception.GetType().Name, exception is PublicTunnelException classified ? classified.Failure : (PublicAccessFailure?)null);
                states[candidate.RuntimeId] = State(candidate.RuntimeId, candidate.Ports, PublicAccessState.Unavailable,
                    exception is PublicTunnelException tunnel ? tunnel.Failure : PublicAccessFailure.GatewayIdentityRejected);
                await statuses.SetAsync(states[candidate.RuntimeId], token);
                if (publications.TryRemove(candidate.RuntimeId, out var stale))
                {
                    try { await docker.RevokeAsync(stale, token); }
                    catch (Exception) { publications[candidate.RuntimeId] = stale; }
                }
            }
        });
        if (docker.RequiresReset) { cleanupNeeded = true; Signal(); }
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

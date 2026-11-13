using Docker.DotNet;
using Docker.DotNet.Models;
using k8s;
using k8s.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using NoCTF.Runtime.Docker;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Docker.Services;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;
using NoCTF.Runtime.Kubernetes.Services;
using NoCTF.Runtime.Kubernetes;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests;

internal sealed class RuntimeExecutionFixture : IAsyncDisposable
{
    public const string Image = "docker.m.daocloud.io/library/alpine@sha256:2e0b81d60597f449baa56dc25d355367869d2dca4cd57e866f1e2b52af3eb55f";
    public const string RunnerId = "capability-execution";
    public const string Pool = "capability-execution";
    public ChallengeApplicationFixture Application { get; private set; } = null!;
    public NoCtfDbContext Db => Application.Db;
    public NatsConnection Connection { get; private set; } = null!;
    public DockerContainerLifecycle Containers { get; private set; } = null!;
    public RuntimeProviderCatalog Providers { get; private set; } = null!;
    public PersistedRunnerCapacityGate Capacity { get; private set; } = null!;
    public IDbContextFactory<NoCtfDbContext> Contexts => services!.GetRequiredService<IDbContextFactory<NoCtfDbContext>>();
    public IOptions<RunnerOptions> RunnerOptions { get; } = Options.Create(new RunnerOptions { Id = RunnerId, Pool = Pool });
    public RuntimeProviderHandler Handler => new(Providers, Reconcilers, RunnerOptions, Capacity, new RuntimeNodeWorkReader(Contexts, TimeProvider.System));
    public IEnumerable<IRuntimeManagedResourceReconciler> Reconcilers => [kubernetesReconciler ?? (IRuntimeManagedResourceReconciler)reconciler!];
    public RuntimeProvider Provider { get; private set; } = RuntimeProvider.Docker;
    public Kubernetes Kubernetes => kubernetes ?? throw new InvalidOperationException("Kubernetes is not initialized.");
    public KubernetesRuntimeOptions KubernetesOptions { get; private set; } = new();
    public string CallbackNetworkName => network + "-callbacks";
    public string CallbackContainerName => network + "-gateway";
    private ServiceProvider? services;
    private DockerClient? docker;
    private Kubernetes? kubernetes;
    private DockerRuntimeResourceReconciler? reconciler;
    private KubernetesRuntimeResourceReconciler? kubernetesReconciler;
    private NatsClusterLease? lease;
    private readonly CancellationTokenSource renewalStop = new();
    private readonly SemaphoreSlim renewalGate = new(1, 1);
    private Task? renewal;
    private int? auxiliaryConcurrencyLimit;
    private RuntimeResourceAmount? registeredResourceLimit;
    private NatsRunnerAvailabilityRegistry registry = null!;
    private readonly RunnerCapacityLedgerCoordinator ledger = new();
    private readonly string network = $"capability-runtime-{Guid.NewGuid():N}";
    private readonly List<Guid> runtimes = [];
    private string? networkId;
    private string? callbackNetworkId;

    public static async Task<RuntimeExecutionFixture> CreateAsync(CancellationToken ct, string publicHost = "localhost", RuntimeProvider provider = RuntimeProvider.Docker)
    {
        var fixture = new RuntimeExecutionFixture();
        fixture.Provider = provider;
        fixture.RunnerOptions.Value.Provider = provider;
        try
        {
            fixture.Application = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Running, ct);
            fixture.services = new ServiceCollection().AddDbContextFactory<NoCtfDbContext>(options =>
                options.UseNpgsql(fixture.Db.Database.GetConnectionString()))
                .AddFusionCache(NoCtfCacheNames.ReadModels).Services.BuildServiceProvider();
            fixture.Connection = new(new NatsOpts { Url = fixture.Application.NatsUrl });
            fixture.registry = new(fixture.Connection, TimeProvider.System);
            fixture.lease = await new NatsClusterLeaseManager(fixture.Connection).TryAcquireAsync(
                NatsClusterLeaseManager.ResourceDomainKey(RunnerId), RunnerId, ct)
                ?? throw new InvalidOperationException("The isolated execution domain has another owner.");
            fixture.renewal = fixture.RenewLeaseAsync(renewalToken: fixture.renewalStop.Token);
            await fixture.RefreshOwnerAsync(ct);
            fixture.Capacity = new(fixture.Db, fixture.registry, fixture.services.GetRequiredService<IFusionCacheProvider>(),
                fixture.Application.Publisher, fixture.ledger);
            var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
                ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
            fixture.docker = new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build();
            fixture.networkId = (await fixture.docker.Networks.CreateNetworkAsync(new NetworksCreateParameters { Name = fixture.network, Driver = "bridge" }, ct)).ID;
            fixture.callbackNetworkId = (await fixture.docker.Networks.CreateNetworkAsync(new NetworksCreateParameters { Name = fixture.CallbackNetworkName, Driver = "bridge", Internal = true }, ct)).ID;
            var options = new DockerRuntimeOptions(Endpoint: endpoint, NetworkName: fixture.network, PublicHost: publicHost,
                CallbackNetworkName: fixture.CallbackNetworkName,
                CallbackContainerName: fixture.CallbackContainerName,
                RegistryConfigDirectory: Path.Combine(AppContext.BaseDirectory, "Fixtures", "Docker"));
            fixture.Containers = new(options); fixture.reconciler = new(options, fixture.Containers);
            var kubernetesOptions = new KubernetesRuntimeOptions();
            if (provider == RuntimeProvider.Kubernetes)
            {
                var context = Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_CONTEXT")
                    ?? throw new InvalidOperationException("An explicit isolated Kubernetes context is required.");
                if (context == "docker-desktop") throw new InvalidOperationException("Use an isolated integration cluster.");
                fixture.kubernetes = new(KubernetesClientConfiguration.BuildConfigFromConfigFile(
                    Environment.GetEnvironmentVariable("KUBECONFIG"), currentContext: context));
                kubernetesOptions = new(Namespace: "noctf-control-" + Guid.NewGuid().ToString("N"), PublicHost: publicHost,
                    PodPidsLimit: 128, ClusterDomain: "cluster.local",
                    ClusterDnsServiceAddress: Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_CLUSTER_DNS")
                        ?? throw new InvalidOperationException("The isolated cluster DNS address is required."),
                    ProtectedCidrs: ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"]);
                await fixture.kubernetes.CoreV1.CreateNamespaceAsync(new() { Metadata = new() { Name = kubernetesOptions.Namespace,
                    Labels = new Dictionary<string, string> { ["noctf.io/purpose"] = "challenge-runtime" } } }, cancellationToken: ct);
                await fixture.kubernetes.NetworkingV1.CreateNamespacedNetworkPolicyAsync(new()
                {
                    Metadata = new() { Name = "baseline-deny" },
                    Spec = new() { PodSelector = new(), PolicyTypes = ["Ingress", "Egress"], Ingress = [], Egress = [] }
                }, kubernetesOptions.Namespace, cancellationToken: ct);
                fixture.kubernetesReconciler = new(fixture.kubernetes, kubernetesOptions);
            }
            else fixture.kubernetes = new(new KubernetesClientConfiguration { Host = "http://127.0.0.1:1" });
            fixture.KubernetesOptions = kubernetesOptions;
            var kubernetesContainers = new KubernetesContainerLifecycle(fixture.kubernetes, kubernetesOptions);
            fixture.Providers = new(fixture.Containers, kubernetesContainers, new DockerContainerRuntime(fixture.Containers, options),
            new KubernetesContainerRuntime(fixture.kubernetes, kubernetesContainers, kubernetesOptions), []);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async Task RefreshOwnerAsync(CancellationToken ct, int? auxiliaryConcurrency = null, RuntimeResourceAmount? resourceLimit = null)
    {
        await renewalGate.WaitAsync(ct);
        try
        {
            auxiliaryConcurrencyLimit = auxiliaryConcurrency;
            registeredResourceLimit = resourceLimit;
            await PublishOwnerAsync(ct);
        }
        finally { renewalGate.Release(); }
    }

    private async Task PublishOwnerAsync(CancellationToken ct)
    {
        await lease!.RenewAsync(ct);
        await registry.PublishHeartbeatAsync(Pool, RunnerId, Provider, TimeSpan.FromMinutes(1), ct);
        var registration = CurrentRunnerRegistration.Create(Pool, RunnerId, registeredResourceLimit ?? new(2L * 1024 * 1024 * 1024, 4000, 4096), provider: Provider,
            resourceDomainFencingToken: lease.FencingToken, processesPerService: Provider == RuntimeProvider.Kubernetes ? 128 : 256);
        if (auxiliaryConcurrencyLimit is { } limit) registration.AdmissionOptions.AuxiliaryConcurrency = limit;
        if (await registry.RegisterAsync(registration, ct) != RunnerAvailabilityRegistrationOutcome.Online)
            throw new InvalidOperationException("The isolated execution Runner was not admitted.");
    }

    private async Task RenewLeaseAsync(CancellationToken renewalToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(renewalToken))
        {
            await renewalGate.WaitAsync(renewalToken);
            try { await PublishOwnerAsync(renewalToken); }
            finally { renewalGate.Release(); }
        }
    }

    public async Task<ProvisionContainerRuntime> QueueAsync(int serviceCount, CancellationToken ct, RuntimeInstance? instance = null)
    {
        await RefreshOwnerAsync(ct);
        instance ??= new TemplateTestRuntimeInstance { Id = Guid.NewGuid(), ChallengeId = Application.TemplateId,
            RuntimeProvider = Provider, RuntimeKind = RuntimeKind.Container, State = RuntimeState.Queued,
            TestFlagDelivery = RuntimeTestFlagDelivery.NotRequired, TestFlagState = RuntimeTestFlagState.NotRequired, CreatedAt = DateTimeOffset.UtcNow };
        var id = instance.Id; Db.RuntimeInstances.Add(instance);
        await Db.SaveChangesAsync(ct);
        var services = Enumerable.Range(0, serviceCount).Select(index => new RuntimeServiceDefinition($"service-{index}", Image, .1m, 64,
            Command: ["nc", "-ll", "-p", "8080", "-e", "/bin/cat"], InternalPorts: [8080])).ToArray();
        var limits = RuntimeResourceBudgetPolicy.Sum(services.Select(service => service.Resources(Provider == RuntimeProvider.Kubernetes ? 128 : 256)));
        return await ClaimAsync(new(id, RunnerId, new(id, Provider, services,
            new Dictionary<string, string> { ["noctf.io/managed"] = "true", ["noctf.io/runtime-instance-id"] = id.ToString("D"), ["noctf.io/job-kind"] = "persistent-runtime" },
            limits, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 8080, "service-0")])), ct);
    }

    public async Task<ProvisionContainerRuntime> ClaimAsync(ProvisionContainerRuntime message, CancellationToken ct)
    {
        await RefreshOwnerAsync(ct);
        if (!runtimes.Contains(message.RuntimeInstanceId)) runtimes.Add(message.RuntimeInstanceId);
        var limits = message.Definition.Limits;
        var claim = await Capacity.TryClaimForRunnerAsync(new(message.RuntimeInstanceId, Pool, limits.MemoryBytes, limits.CpuMillicores, limits.PidsLimit,
            ProcessesPerService: Provider == RuntimeProvider.Kubernetes ? 128 : 256), RunnerId, ct);
        if (claim.Availability != RunnerCapacityAvailability.Claimed) throw new InvalidOperationException($"Execution capacity rejected: {claim.Failure}.");
        var runtime = await Db.RuntimeInstances.SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        if (runtime.State != RuntimeState.Queued) throw new InvalidOperationException("Only a queued Runtime can be claimed by this execution fixture.");
        runtime.RunnerId = RunnerId; runtime.State = RuntimeState.Provisioning; await Db.SaveChangesAsync(ct);
        return message;
    }

    public async Task PersistAsync(RuntimeProvisioned provisioned, CancellationToken ct)
    {
        await new RuntimeProvisionWriteBackMessageHandler(Db, Application.Publisher, new CompetitionEventStore(Db, Application.Publisher),
            TimeProvider.System, Capacity).Handle(provisioned, ct);
    }

    public PersistedRunnerCapacityGate CapacityFor(NoCtfDbContext db) => new(db, registry,
        services!.GetRequiredService<IFusionCacheProvider>(), new WolverinePostCommitMessagePublisher(Application.MessageBus, db), ledger);

    public async ValueTask DisposeAsync()
    {
        await renewalStop.CancelAsync();
        if (renewal is not null)
        {
            try { await renewal; }
            catch (OperationCanceledException) when (renewalStop.IsCancellationRequested) { }
        }
        if (kubernetesReconciler is not null)
        {
            foreach (var id in runtimes)
                await kubernetesReconciler.DestroyByIdentityAsync(new(id), RuntimeTerminationMode.Force, RunnerOptions.Value.Cleanup.ToPolicy(), CancellationToken.None);
            await kubernetes!.CoreV1.DeleteNamespaceAsync(KubernetesOptions.Namespace, cancellationToken: CancellationToken.None);
        }
        if (reconciler is not null)
        {
            foreach (var id in runtimes)
                await reconciler.DestroyByIdentityAsync(new(id), RuntimeTerminationMode.Force, RunnerOptions.Value.Cleanup.ToPolicy(), CancellationToken.None);
            reconciler.Dispose();
        }
        Containers?.Dispose(); kubernetes?.Dispose();
        if (callbackNetworkId is not null) await docker!.Networks.DeleteNetworkAsync(callbackNetworkId);
        if (networkId is not null) await docker!.Networks.DeleteNetworkAsync(networkId);
        docker?.Dispose();
        if (lease is not null) await lease.DisposeAsync();
        if (Connection is not null) await Connection.DisposeAsync();
        if (services is not null) await services.DisposeAsync();
        ledger.Dispose();
        if (Application is not null) await Application.DisposeAsync();
        renewalGate.Dispose(); renewalStop.Dispose();
    }
}

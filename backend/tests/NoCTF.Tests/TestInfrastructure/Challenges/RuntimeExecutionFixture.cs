using Docker.DotNet;
using Docker.DotNet.Models;
using k8s;
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
    public RuntimeProviderHandler Handler => new(Providers, [reconciler!], RunnerOptions, Capacity, new RuntimeNodeWorkReader(Contexts, TimeProvider.System));
    public IEnumerable<IRuntimeManagedResourceReconciler> Reconcilers => [reconciler!];
    public string CallbackNetworkName => network + "-callbacks";
    private ServiceProvider? services;
    private DockerClient? docker;
    private Kubernetes? kubernetes;
    private DockerRuntimeResourceReconciler? reconciler;
    private NatsClusterLease? lease;
    private NatsRunnerAvailabilityRegistry registry = null!;
    private readonly RunnerCapacityLedgerCoordinator ledger = new();
    private readonly string network = $"capability-runtime-{Guid.NewGuid():N}";
    private readonly List<Guid> runtimes = [];
    private string? networkId;
    private string? callbackNetworkId;

    public static async Task<RuntimeExecutionFixture> CreateAsync(CancellationToken ct, string publicHost = "localhost")
    {
        var fixture = new RuntimeExecutionFixture();
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
                RegistryConfigDirectory: Path.Combine(AppContext.BaseDirectory, "Fixtures", "Docker"));
            fixture.Containers = new(options); fixture.reconciler = new(options, fixture.Containers);
            fixture.kubernetes = new(new KubernetesClientConfiguration { Host = "http://127.0.0.1:1" });
            var kubernetesOptions = new KubernetesRuntimeOptions();
            var kubernetesContainers = new KubernetesContainerLifecycle(fixture.kubernetes, kubernetesOptions);
            fixture.Providers = new(fixture.Containers, kubernetesContainers, new DockerContainerRuntime(fixture.Containers, options),
                new KubernetesContainerRuntime(fixture.kubernetes, kubernetesContainers, kubernetesOptions), []);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async Task RefreshOwnerAsync(CancellationToken ct, int? auxiliaryConcurrency = null, RuntimeResourceAmount? resourceLimit = null)
    {
        await lease!.RenewAsync(ct);
        await registry.PublishHeartbeatAsync(Pool, RunnerId, RuntimeProvider.Docker, TimeSpan.FromMinutes(1), ct);
        var registration = CurrentRunnerRegistration.Create(Pool, RunnerId, resourceLimit ?? new(2L * 1024 * 1024 * 1024, 4000, 4096),
            resourceDomainFencingToken: lease.FencingToken);
        if (auxiliaryConcurrency is { } limit) registration.AdmissionOptions.AuxiliaryConcurrency = limit;
        if (await registry.RegisterAsync(registration, ct) != RunnerAvailabilityRegistrationOutcome.Online)
            throw new InvalidOperationException("The isolated execution Runner was not admitted.");
    }

    public async Task<ProvisionContainerRuntime> QueueAsync(int serviceCount, CancellationToken ct, RuntimeInstance? instance = null)
    {
        await RefreshOwnerAsync(ct);
        instance ??= new TemplateTestRuntimeInstance { Id = Guid.NewGuid(), ChallengeId = Application.TemplateId,
            RuntimeProvider = RuntimeProvider.Docker, RuntimeKind = RuntimeKind.Container, State = RuntimeState.Queued,
            TestFlagDelivery = RuntimeTestFlagDelivery.NotRequired, TestFlagState = RuntimeTestFlagState.NotRequired, CreatedAt = DateTimeOffset.UtcNow };
        var id = instance.Id; Db.RuntimeInstances.Add(instance);
        await Db.SaveChangesAsync(ct);
        var services = Enumerable.Range(0, serviceCount).Select(index => new RuntimeServiceDefinition($"service-{index}", Image, .1m, 64,
            Command: ["nc", "-ll", "-p", "8080", "-e", "/bin/cat"], InternalPorts: [8080])).ToArray();
        var limits = RuntimeResourceBudgetPolicy.Sum(services.Select(service => service.Resources(256)));
        return await ClaimAsync(new(id, RunnerId, new(id, RuntimeProvider.Docker, services,
            new Dictionary<string, string> { ["noctf.io/managed"] = "true", ["noctf.io/runtime-instance-id"] = id.ToString("D"), ["noctf.io/job-kind"] = "persistent-runtime" },
            limits, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 8080, "service-0")])), ct);
    }

    public async Task<ProvisionContainerRuntime> ClaimAsync(ProvisionContainerRuntime message, CancellationToken ct)
    {
        await RefreshOwnerAsync(ct);
        if (!runtimes.Contains(message.RuntimeInstanceId)) runtimes.Add(message.RuntimeInstanceId);
        var limits = message.Definition.Limits;
        var claim = await Capacity.TryClaimForRunnerAsync(new(message.RuntimeInstanceId, Pool, limits.MemoryBytes, limits.CpuMillicores, limits.PidsLimit), RunnerId, ct);
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
    }
}

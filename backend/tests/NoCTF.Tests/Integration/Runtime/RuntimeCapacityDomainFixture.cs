using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Runtime;

/// <summary>Real isolated persistence/coordination; no provider workloads or production endpoints.</summary>
internal sealed class RuntimeCapacityDomainFixture : IAsyncDisposable
{
    internal const string PostgresImage = "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193";
    internal const string NatsImage = "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d";
    internal const string Pool = "domain-tests";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder(
        "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
    private readonly IContainer nats = new ContainerBuilder(NatsImage)
        .WithPortBinding(4222, assignRandomHostPort: true).WithCommand("-js")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
    private readonly ServiceProvider cacheServices = new ServiceCollection()
        .AddFusionCache(NoCtfCacheNames.ReadModels).Services.BuildServiceProvider();
    private readonly Dictionary<string, (NatsClusterLease Lease, RunnerAvailabilityRegistration Registration)> owners = [];
    private NatsConnection connection = null!;

    public DbContextOptions<NoCtfDbContext> Options { get; private set; } = null!;
    public NatsRunnerAvailabilityRegistry Registry { get; private set; } = null!;
    public RunnerCapacityLedgerCoordinator Coordinator { get; } = new();
    public Guid[] RuntimeIds { get; private set; } = [];

    public static async Task<RuntimeCapacityDomainFixture> CreateAsync(
        int runtimeCount, CancellationToken ct, IInterceptor[]? interceptors = null, bool previousSchema = false)
    {
        var fixture = new RuntimeCapacityDomainFixture();
        try
        {
            await Task.WhenAll(fixture.postgres.StartAsync(ct), fixture.nats.StartAsync(ct));
            fixture.connection = new NatsConnection(new NatsOpts
            {
                Url = $"nats://{fixture.nats.Hostname}:{fixture.nats.GetMappedPublicPort(4222)}"
            });
            fixture.Registry = new NatsRunnerAvailabilityRegistry(fixture.connection, TimeProvider.System);
            fixture.Options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(fixture.postgres.GetConnectionString(), provider =>
                    provider.MigrationsAssembly(typeof(NoCTF.Persistence.PostgreSql.PostgreSqlPersistence).Assembly.FullName))
                .UseSnakeCaseNamingConvention().AddInterceptors(interceptors ?? []).Options;
            await using var db = new NoCtfDbContext(fixture.Options);
            if (previousSchema)
                await db.GetService<IMigrator>().MigrateAsync("20261013124256_MergeAwdAndTheoryModels", ct);
            else
                await db.Database.EnsureCreatedAsync(ct);
            fixture.RuntimeIds = await SeedAsync(db, runtimeCount, ct);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public async Task AddOwnerAsync(string runner, string domain, RuntimeResourceAmount capacity,
        int startupLimit, CancellationToken ct)
    {
        var lease = await new NatsClusterLeaseManager(connection).TryAcquireAsync(
            NatsClusterLeaseManager.ResourceDomainKey(domain), runner, ct)
            ?? throw new InvalidOperationException("The test resource domain already has an owner.");
        var registration = CurrentRunnerRegistration.Create(Pool, runner, capacity,
            resourceDomainFencingToken: lease.FencingToken);
        registration = registration with
        {
            Admission = registration.Admission with
            {
                Observation = registration.Admission.Observation! with { ResourceDomain = domain }
            },
            AdmissionOptions = new RunnerAdmissionOptions { MainStartupConcurrency = startupLimit }
        };
        owners.Add(runner, (lease, registration));
        await PublishAsync(registration, ct);
    }

    public async Task ReleaseOwnerAsync(string runner)
    {
        var owner = owners[runner];
        owners.Remove(runner);
        await owner.Lease.DisposeAsync();
    }

    public async Task RefreshOwnersAsync(CancellationToken ct)
    {
        foreach (var runner in owners.Keys.ToArray())
        {
            var owner = owners[runner];
            await owner.Lease.RenewAsync(ct);
            var registration = owner.Registration with
            {
                ResourceDomainFencingToken = owner.Lease.FencingToken,
                Admission = owner.Registration.Admission with
                {
                    Observation = owner.Registration.Admission.Observation! with { ObservedAt = DateTimeOffset.UtcNow }
                }
            };
            owners[runner] = (owner.Lease, registration);
            await PublishAsync(registration, ct);
        }
    }

    private async Task PublishAsync(RunnerAvailabilityRegistration registration, CancellationToken ct)
    {
        await Registry.PublishHeartbeatAsync(Pool, registration.RunnerId,
            RuntimeProvider.Docker, TimeSpan.FromMinutes(1), ct);
        if (await Registry.RegisterAsync(registration, ct) != RunnerAvailabilityRegistrationOutcome.Online)
            throw new InvalidOperationException("The isolated test Runner registration is not ready.");
    }

    public PersistedRunnerCapacityGate Gate(NoCtfDbContext db, RunnerCapacityLedgerCoordinator? coordinator = null,
        IPostCommitMessagePublisher? publisher = null) =>
        new(db, Registry, cacheServices.GetRequiredService<IFusionCacheProvider>(), publisher ?? new CapacityOutbox(), coordinator ?? Coordinator);

    public GlobalLedgerCapacityBaseline BaselineGate(NoCtfDbContext db, GlobalCapacityLedgerCoordinator coordinator) =>
        new(db, Registry, cacheServices.GetRequiredService<IFusionCacheProvider>(), new CapacityOutbox(), coordinator);

    public async Task<RunnerCapacityClaim> ClaimAsync(Guid id, string runner, CancellationToken ct,
        RunnerCapacityLedgerCoordinator? coordinator = null)
    {
        await using var db = new NoCtfDbContext(Options);
        return await Gate(db, coordinator).TryClaimForRunnerAsync(new(id, Pool, 128, 10, 1), runner, ct);
    }

    public async Task<RunnerCapacityReleaseOutcome> ReleaseAsync(Guid id, string runner, CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(Options);
        return await Gate(db).ReleaseAsync(id, runner, ct);
    }

    internal static async Task<Guid[]> SeedAsync(NoCtfDbContext db, int runtimeCount, CancellationToken ct)
    {
        var ownerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new User
        {
            Id = ownerId, UserName = $"capacity-{ownerId:N}", NormalizedUserName = $"CAPACITY-{ownerId:N}",
            Email = $"{ownerId:N}@example.test", PasswordHash = "unused", Kind = UserKind.Human,
            Role = UserRole.Administrator, AccountStatus = UserAccountStatus.Active, CreatedAt = now, UpdatedAt = now
        });
        var ids = Enumerable.Range(0, runtimeCount).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids)
        {
            var challengeId = Guid.NewGuid();
            db.Challenges.Add(new AwdpChallenge
            {
                Id = challengeId, OwnerId = ownerId, Title = "Capacity fixture", Direction = "Pwn",
                Definition = TestConfigurations.Definition(GameMode.Awdp), CreatedAt = now, UpdatedAt = now
            });
            db.RuntimeInstances.Add(new TemplateTestRuntimeInstance
            {
                Id = id, ChallengeId = challengeId, RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker, State = RuntimeState.Queued,
                TestFlagDelivery = RuntimeTestFlagDelivery.NotRequired, TestFlagState = RuntimeTestFlagState.NotRequired,
                CreatedAt = now
            });
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return ids;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var owner in owners.Values) await owner.Lease.DisposeAsync();
        if (connection is not null) await connection.DisposeAsync();
        Coordinator.Dispose();
        await cacheServices.DisposeAsync();
        await nats.DisposeAsync();
        await postgres.DisposeAsync();
    }

    private sealed class CapacityOutbox : IPostCommitMessagePublisher
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset at) => throw new NotSupportedException();
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset at) where T : IRunnerNodeMessage => throw new NotSupportedException();
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}

using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using NoCTF.Infrastructure.Challenges.Testing;
using NoCTF.GameModes.Registration;
using NoCTF.Runner;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runtime.Docker;
using NoCTF.Runtime.Docker.Compose;
using NoCTF.Runtime.Docker.Containers;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Wolverine;
using Wolverine.Nats;
using Wolverine.Postgresql;
using Wolverine.EntityFrameworkCore;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration"), NotInParallel]
public sealed class CapacityCrashRecoveryTests
{
    [Test, Arguments(CapacityCrashWindow.RollbackThenCancel), Arguments(CapacityCrashWindow.BeforeAllocationWrite),
        Arguments(CapacityCrashWindow.AfterAllocationCommit), Arguments(CapacityCrashWindow.AfterProviderCreate),
        Arguments(CapacityCrashWindow.AfterReleaseCommit), Timeout(600_000)]
    public async Task Real_observation_and_durable_workers_converge_after_faults(CapacityCrashWindow window, CancellationToken ct)
    {
        var image = Environment.GetEnvironmentVariable("NOCTF_CAPACITY_FAULT_IMAGE");
        if (string.IsNullOrWhiteSpace(image))
        {
            Skip.Test("Set NOCTF_CAPACITY_FAULT_IMAGE to a local ASP.NET 10 Linux image for daemon-host process fault testing.");
            return;
        }
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var cache = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await using var nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithCommand("-js").WithPortBinding(4222, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await Task.WhenAll(postgres.StartAsync(ct), cache.StartAsync(ct), nats.StartAsync(ct));
            await using var redis = await ConnectionMultiplexer.ConnectAsync(cache.GetConnectionString());
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var id = Guid.NewGuid();
            var (ownerId, challengeId) = await SeedAsync(db, id, ct);
            var output = Environment.GetEnvironmentVariable("NOCTF_CAPACITY_MEASUREMENTS") ?? Path.Combine(Path.GetTempPath(), "noctf-capacity-faults");
            var directory = Path.Combine(output, window + "-" + id.ToString("N"));
            Directory.CreateDirectory(directory);
            var externalHost = "host.docker.internal";
            var childPostgres = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Host = externalHost }.ConnectionString;
            var runner = CapacityCrashProcessHostTests.RunnerId;
            var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
                ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
            using var docker = new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build();
            var dockerOptions = new DockerRuntimeOptions(Endpoint: endpoint);
            using var inventory = new DockerRuntimeResourceReconciler(dockerOptions, new DockerComposeRuntime(dockerOptions));
            var producerConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:PostgreSql"] = postgres.GetConnectionString() }).Build();
            using var producer = Host.CreateDefaultBuilder().ConfigureServices(services => services.AddNoCtfStandaloneRunnerPersistence(producerConfiguration))
                .UseWolverine(configuration =>
            {
                configuration.Discovery.DisableConventionalDiscovery();
                configuration.PersistMessagesWithPostgresql(postgres.GetConnectionString(), "capacity_fault_producer");
                configuration.AutoBuildMessageStorageOnStartup = AutoCreate.All;
                configuration.UseEntityFrameworkCoreTransactions();
                configuration.UseNats($"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}").AutoProvision().UseJetStream(_ => { })
                    .DefineWorkQueueStream(CapacityCrashProcessHostTests.ControlStream,
                        stream => stream.WithSubject(CapacityCrashProcessHostTests.ControlSubject), CapacityCrashProcessHostTests.ControlSubject);
                configuration.PublishMessage<StopRuntime>().ToNatsSubject(CapacityCrashProcessHostTests.ControlSubject)
                    .UseJetStream(CapacityCrashProcessHostTests.ControlStream).UseDurableOutbox();
            }).Build();
            await producer.StartAsync(ct);
            IContainer? child = null;
            var observations = new List<string>();
            ContainerBuilder Child(CapacityCrashWindow at) => new ContainerBuilder(image)
                .WithEntrypoint("dotnet").WithCommand("/tests/NoCTF.Tests.dll", "--treenode-filter",
                    "/*/*/CapacityCrashProcessHostTests/Run", "--minimum-expected-tests", "1")
                .WithBindMount(AppContext.BaseDirectory, "/tests", AccessMode.ReadWrite)
                .WithBindMount(directory, "/checkpoints", AccessMode.ReadWrite)
                .WithEnvironment("NOCTF_CAPACITY_CHILD", "true")
                .WithEnvironment("ConnectionStrings__PostgreSql", childPostgres)
                .WithEnvironment("ConnectionStrings__Redis", $"{externalHost}:{cache.GetMappedPublicPort(6379)},abortConnect=false")
                .WithEnvironment("ConnectionStrings__Nats", $"nats://{externalHost}:{nats.GetMappedPublicPort(4222)}")
                .WithEnvironment("CapacityFixture__RuntimeId", id.ToString())
                .WithEnvironment("CapacityFixture__Window", at.ToString())
                .WithEnvironment("Runner__Id", runner).WithEnvironment("Runner__Pool", "fault-tests")
                .WithEnvironment("Runner__Provider", "Docker")
                .WithEnvironment("Runner__Heartbeat__IntervalSeconds", "5")
                .WithEnvironment("Runner__Heartbeat__TtlSeconds", "15")
                .WithEnvironment("Runtime__Docker__Endpoint", "unix:///var/run/docker.sock")
                .WithEnvironment("RunnerScoring__CallbackBaseUrl", "http://127.0.0.1:8080")
                .WithEnvironment("RunnerScoring__SigningKey", new string('x', 64))
                .WithCreateParameterModifier(parameters =>
                {
                    parameters.User = "0";
                    parameters.HostConfig!.Binds = ["/var/run/docker.sock:/var/run/docker.sock",
                        "/proc:/host/proc:ro", "/sys/fs/cgroup:/host/sys/fs/cgroup:ro", "/etc/machine-id:/host/etc/machine-id:ro"];
                });
            async Task<RuntimeState> StateAsync() => await db.RuntimeInstances.AsNoTracking().Where(row => row.Id == id).Select(row => row.State).SingleAsync(ct);
            async Task CaptureAsync(string stage)
            {
                var fields = await redis.GetDatabase().HashGetAllAsync($"runner:{runner}:capacity");
                observations.Add(JsonSerializer.Serialize(new { stage, at = DateTimeOffset.UtcNow, fields = fields.ToDictionary(item => item.Name.ToString(), item => item.Value.ToString()) }));
                await File.WriteAllLinesAsync(Path.Combine(directory, "observations.jsonl"), observations, ct);
            }
            async Task RequestStopAsync()
            {
                await using var scope = producer.Services.CreateAsyncScope();
                var store = new ChallengeTestRuntimeStore(scope.ServiceProvider.GetRequiredService<NoCtfDbContext>(),
                    new ChallengeRuntimeTemplateCatalog(), new FixedRuntimePlacementPolicy(runnerPool: "fault-tests"),
                    scope.ServiceProvider.GetRequiredService<ITransactionalMessageOutbox>());
                await store.MutateAsync(new(challengeId, ownerId, true, RuntimeAction.Stop, null, DateTimeOffset.UtcNow), ct);
            }
            async Task WaitForAsync(Func<Task<bool>> condition, string stage) => await WaitAsync(async () =>
            {
                if (child is not null && (await docker.Containers.InspectContainerAsync(child.Id, ct)).State?.Running != true)
                    throw new InvalidOperationException("Capacity child exited before " + stage + "; inspect host-events.log.");
                return await condition();
            }, stage, ct);
            try
            {
                child = Child(window).Build();
                await child.StartAsync(ct);
                if (window == CapacityCrashWindow.AfterReleaseCommit)
                {
                    await WaitForAsync(async () => await StateAsync() == RuntimeState.Running, "initial runtime creation");
                    await CaptureAsync("running-before-stop");
                    await RequestStopAsync();
                }
                await WaitForAsync(() => Task.FromResult(File.Exists(Path.Combine(directory, "fault"))), "fault checkpoint");
                await CaptureAsync("fault-checkpoint");
                if (window != CapacityCrashWindow.RollbackThenCancel)
                {
                    await docker.Containers.KillContainerAsync(child.Id, new ContainerKillParameters { Signal = "SIGKILL" }, ct);
                    var logs = await child.GetLogsAsync(ct: ct);
                    await File.WriteAllTextAsync(Path.Combine(directory, "killed-host.log"), logs.Stdout + logs.Stderr, ct);
                    await child.DisposeAsync();
                    child = null;
                }
                if (window is CapacityCrashWindow.RollbackThenCancel or CapacityCrashWindow.BeforeAllocationWrite)
                    await RequestStopAsync();
                if (child is null)
                {
                    child = Child(CapacityCrashWindow.None).Build();
                    await child.StartAsync(ct);
                }
                if (window is CapacityCrashWindow.AfterAllocationCommit or CapacityCrashWindow.AfterProviderCreate)
                {
                    await WaitForAsync(async () => await StateAsync() == RuntimeState.Running, "recovered runtime creation");
                    var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters
                    {
                        All = true, Filters = new Dictionary<string, IDictionary<string, bool>>
                        { ["label"] = new Dictionary<string, bool> { [$"noctf.io/runtime-instance-id={id:D}"] = true } }
                    }, ct);
                    await Assert.That(containers.Count).IsEqualTo(1);
                    await CaptureAsync("recovered-running");
                    await RequestStopAsync();
                }
                await WaitForAsync(async () => await StateAsync() == RuntimeState.Stopped
                    && (long?)await redis.GetDatabase().HashGetAsync($"runner:{runner}:capacity", "startupReservedMemoryBytes") == 0,
                    "automatic cleanup and startup reservation recovery");
                await WaitForAsync(async () => (string?)await redis.GetDatabase().HashGetAsync($"runner:{runner}:capacity", "admissionState") == "ready",
                    "healthy admission after recovery");
                await Assert.That(await inventory.WorkloadExistsAsync(new(RuntimeWorkloadKind.Runtime, id, id), ct)).IsFalse();
                await Assert.That((await db.RuntimeInstances.AsNoTracking().SingleAsync(row => row.Id == id, ct)).CapacityAllocations.Items).IsEmpty();
                await Assert.That((long)await redis.GetDatabase().HashGetAsync($"runner:{runner}:capacity", "startingPrimary")).IsEqualTo(0);
                await CaptureAsync("converged");
                await File.WriteAllLinesAsync(Path.Combine(directory, "observations.jsonl"), observations, ct);
            }
            finally
            {
                if (child is not null)
                {
                    await child.StopAsync(CancellationToken.None);
                    var logs = await child.GetLogsAsync();
                    await File.WriteAllTextAsync(Path.Combine(directory, "final-host.log"), logs.Stdout + logs.Stderr);
                    await child.DisposeAsync();
                }
                await inventory.DestroyByIdentityAsync(new(id), CancellationToken.None);
                await producer.StopAsync(CancellationToken.None);
            }
        });
    }

    private static async Task WaitAsync(Func<Task<bool>> condition, string stage, CancellationToken ct)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(150);
        while (!await condition())
        {
            if (DateTimeOffset.UtcNow >= until) throw new TimeoutException("Timed out waiting for " + stage);
            await Task.Delay(200, ct);
        }
    }

    private static async Task<(Guid OwnerId, Guid ChallengeId)> SeedAsync(NoCtfDbContext db, Guid runtimeId, CancellationToken ct)
    {
        var owner = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new User { Id = owner, UserName = "fault-owner", NormalizedUserName = "FAULT-OWNER", Email = "fault@example.test",
            PasswordHash = "fixture", Role = UserRole.Administrator, AccountStatus = UserAccountStatus.Active, CreatedAt = now, UpdatedAt = now });
        db.Challenges.Add(new Challenge
        {
            Id = challenge, OwnerId = owner, Mode = GameMode.Ctf, Title = "Fault fixture", CreatedAt = now, UpdatedAt = now,
            DefinitionJson = JsonSerializer.Serialize(new CtfChallengeConfiguration(CtfChallengeConfiguration.CurrentSchemaVersion, null, null,
                Runtime: new ChallengeRuntimeTemplate(RuntimeAllocation.PerTeam, new ContainerRuntimeDefinition("busybox:1.36.1", ["sleep", "600"]),
                    new(64 * 1024 * 1024, 200_000_000, 64))), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId, ChallengeId = challenge, Purpose = RuntimePurpose.TemplateTest, RuntimeProvider = RuntimeProvider.Docker,
            RuntimeKind = RuntimeKind.Container, State = RuntimeState.Queued, CreatedAt = now,
            TestFlagDelivery = RuntimeTestFlagDelivery.NotRequired, TestFlagState = RuntimeTestFlagState.NotRequired
        });
        await db.SaveChangesAsync(ct);
        return (owner, challenge);
    }
}

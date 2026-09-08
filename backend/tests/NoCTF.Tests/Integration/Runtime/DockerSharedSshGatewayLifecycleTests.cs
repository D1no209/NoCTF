using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Platform;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.PublicAccess;
using NoCTF.Runner.PublicAccess;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Docker.PublicAccess;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), Category("GatewayLifecycle"), NotInParallel]
public sealed class DockerSharedSshGatewayLifecycleTests
{
    [Test]
    public async Task Lease_directories_reject_unsafe_roots_and_do_not_follow_nested_links_during_cleanup(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux filesystem semantics required.");
        if (!OperatingSystem.IsLinux()) return;
        var root = Directory.CreateTempSubdirectory("noctf-lease-directory-");
        var outside = Directory.CreateTempSubdirectory("noctf-lease-outside-");
        try
        {
            var marker = Path.Combine(outside.FullName, "keep.txt");
            await File.WriteAllTextAsync(marker, "unchanged", ct);
            File.SetUnixFileMode(root.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupWrite);
            await Assert.That(() => new GatewayLeaseDirectory(root.FullName, "/isolated-host-state")).Throws<ArgumentException>();
            File.SetUnixFileMode(root.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var store = new GatewayLeaseDirectory(root.FullName, "/isolated-host-state");
            var publication = Guid.NewGuid();
            await store.CreateAsync(publication, ct);
            Directory.CreateSymbolicLink(Path.Combine(store.LocalPublication(publication), "listen", "outside"), outside.FullName);
            store.Remove(store.SessionId, publication);
            store.Invalidate(publication); // Repeated stale-owner cleanup is harmless.
            await Assert.That(await File.ReadAllTextAsync(marker, ct)).IsEqualTo("unchanged");
        }
        finally { root.Delete(recursive: true); outside.Delete(recursive: true); }
    }

    [Test, Timeout(240_000)]
    public async Task Actual_linux_agent_renews_during_slow_control_rechecks_bans_and_hands_over_safely(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Run this bind-permission test in the documented isolated Linux test runner.");
        await DockerIntegrationTest.RunAsync(async () =>
        {
            var stateRoot = Environment.GetEnvironmentVariable("NOCTF_GATEWAY_LOCAL_STATE_ROOT")
                ?? throw new InvalidOperationException("Set NOCTF_GATEWAY_LOCAL_STATE_ROOT to a private writable Linux bind mount.");
            var hostRoot = Environment.GetEnvironmentVariable("NOCTF_GATEWAY_HOST_STATE_ROOT")
                ?? throw new InvalidOperationException("Set NOCTF_GATEWAY_HOST_STATE_ROOT to the matching Docker host directory.");
            var runtimeId = Guid.NewGuid();
            var connector = "ssh-lab-" + Guid.NewGuid().ToString("N");
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var redisContainer = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(postgres.StartAsync(ct), redisContainer.StartAsync(ct));
            await using var redis = await ConnectionMultiplexer.ConnectAsync(redisContainer.GetConnectionString());
            using var docker = new DockerClientBuilder().WithEndpoint(new Uri("unix:///var/run/docker.sock")).Build();
            var bridge = await docker.Networks.InspectNetworkAsync("bridge", ct);
            var serverHost = Environment.GetEnvironmentVariable("TESTCONTAINERS_HOST_OVERRIDE")
                ?? bridge.IPAM.Config.Single(item => item.Gateway is not null).Gateway;
            var hostKey = Key(); var clientKey = Key();
            await using var server = new ContainerBuilder("noctf-gateway-ssh-prototype:local")
                .WithPortBinding(60999, true).WithPortBinding(40000, true).WithPortBinding(40001, true)
                .WithResourceMapping(hostKey.Private, "/config/host_key", 0, 0, DotNet.Testcontainers.Configurations.UnixFileModes.UserRead)
                .WithResourceMapping(Bytes("restrict,port-forwarding " + clientKey.Public), "/config/authorized_keys")
                .WithResourceMapping(Bytes(ServerConfig), "/config/sshd_config")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(60999)).Build();
            await server.StartAsync(ct);
            await using var target = new ContainerBuilder(PublicGatewaySafetyPrototypeTests.Image)
                .WithLabel("noctf.io/runtime-instance-id", runtimeId.ToString("D"))
                .WithPortBinding(8080, true).WithResourceMapping(Bytes(TargetScript), "/target.py")
                .WithEntrypoint("python", "/target.py")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080)).Build();
            await target.StartAsync(ct);
            var hostPort = target.GetMappedPublicPort(8080);
            var privateDirectory = Directory.CreateTempSubdirectory("noctf-ssh-lifecycle-keys-");
            try
            {
                var privateKeyPath = Path.Combine(privateDirectory.FullName, "key");
                var knownHostsPath = Path.Combine(privateDirectory.FullName, "known_hosts");
                await File.WriteAllBytesAsync(privateKeyPath, clientKey.Private, ct);
                await File.WriteAllTextAsync(knownHostsPath, $"[{serverHost}]:{server.GetMappedPublicPort(60999)} {hostKey.Public}", ct);
                var options = new SharedSshGatewayOptions(connector, "test-runner", "noctf-gateway-ssh-client:local", "noctf-gateway-relay:local",
                    serverHost, server.GetMappedPublicPort(60999), privateKeyPath, knownHostsPath, stateRoot, hostRoot, [40000, 40001]);
                var capability = new PublicGatewayCapability(connector, "test-runner", ["https://public.example.test"], 40000, 40001, [], 2, true,
                    Transport: PublicGatewayTransportKind.SharedSsh);
                var policy = new PublicGatewayPolicy(true, connector, capability.ApprovedOrigins[0], ["https://direct.example.test"], serverHost, null, 2);
                var services = new ServiceCollection();
                services.AddDbContext<NoCtfDbContext>(builder => builder.UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
                services.AddSingleton(TimeProvider.System); services.AddScoped<PublicGatewayLeaseGuard>();
                services.AddFusionCache(NoCtfCacheNames.ReadModels);
                using var provider = services.BuildServiceProvider();
                var statuses = new PublicGatewayStatusStore(provider.GetRequiredService<IFusionCacheProvider>(), TimeProvider.System);
                await SeedAsync(provider, capability, policy, runtimeId, target.Id, hostPort, ct);
                using var firstTransport = new DockerSharedSshGateway(new("unix:///var/run/docker.sock"), options, TimeProvider.System);
                var slow = new SlowTransport(firstTransport);
                using var logs = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
                using var first = new PublicGatewayAgent(provider.GetRequiredService<IServiceScopeFactory>(), slow, capability, statuses, redis,
                    TimeProvider.System, logs.CreateLogger<PublicGatewayAgent>());
                using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
                await first.StartAsync(ct);
                try
                {
                    var ready = await ReadyAsync(statuses, runtimeId, ct);
                    await Assert.That(ready.Endpoints.Single().HostPort).IsEqualTo(hostPort);
                    var publicPort = ready.Endpoints.Single().PublicPort!.Value;
                    var address = new Uri($"http://{server.Hostname}:{server.GetMappedPublicPort(publicPort)}/");
                    await Assert.That(await http.GetStringAsync(address, ct)).IsEqualTo("linux-target");
                    var sharedClient = (await ResourcesAsync(docker, connector, ct)).Single(item => item.Labels["noctf.io/gateway-role"] == "client");
                    await Assert.That((await docker.Containers.InspectContainerAsync(sharedClient.ID, ct)).HostConfig!.Init ?? false).IsTrue();
                    var controlProbe = new DockerSshControl(docker, sharedClient.ID);
                    for (var probe = 0; probe < 80; probe++)
                    {
                        await Assert.That(await controlProbe.ReadSessionAsync(ct)).IsNotNull();
                        await Task.Delay(250, ct); // bounded live watchdog concurrency, but >64 lifetime execs
                    }
                    await Task.Delay(5000, ct); // all four-second timeout watchdogs have exited
                    var processCheck = await docker.Exec.CreateContainerExecAsync(sharedClient.ID,
                        new ContainerExecCreateParameters { Cmd = ["cat", "/sys/fs/cgroup/pids.current"], AttachStdout = true }, ct);
                    using (var stream = await docker.Exec.StartContainerExecAsync(processCheck.ID, new ContainerExecStartParameters(), ct))
                    {
                        var (count, _) = await stream.ReadOutputToEndAsync(ct);
                        await Assert.That(int.Parse(count.Trim(), System.Globalization.CultureInfo.InvariantCulture)).IsLessThan(24);
                    }
                    Console.WriteLine("Repeated SSH control probes: no zombie/PID accumulation PASS.");
                    var oldRelay = (await ResourcesAsync(docker, connector, ct)).Single(item => item.Labels["noctf.io/gateway-role"] == "relay");
                    var inspected = await docker.Containers.InspectContainerAsync(oldRelay.ID, ct);
                    await Assert.That(inspected.Config!.User.StartsWith("0:", StringComparison.Ordinal)).IsFalse();
                    await Assert.That(inspected.HostConfig!.Binds!.Any(bind => bind.EndsWith(":/lease:ro", StringComparison.Ordinal))).IsTrue();
                    await Assert.That(inspected.HostConfig.Binds.Any(bind => bind.Contains("docker.sock", StringComparison.Ordinal))).IsFalse();

                    slow.DelayNext = true; first.Signal();
                    await slow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                    await Task.Delay(TimeSpan.FromSeconds(10), ct); // longer than the nine-second endpoint/owner leases
                    await Assert.That((await docker.Containers.InspectContainerAsync(oldRelay.ID, ct)).State?.Running ?? false).IsTrue();
                    await Assert.That(await http.GetStringAsync(address, ct)).IsEqualTo("linux-target");
                    await Assert.That(await redis.GetDatabase().KeyExistsAsync("noctf:public-gateway:owner:" + connector)).IsTrue();
                    await slow.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);

                    using (var scope = provider.CreateScope())
                        await scope.ServiceProvider.GetRequiredService<NoCtfDbContext>().Teams.ExecuteUpdateAsync(update => update.SetProperty(team => team.IsBanned, true), ct);
                    first.Signal();
                    await WaitAsync(async () => !(await ResourcesAsync(docker, connector, ct)).Any(item => item.Labels["noctf.io/gateway-role"] == "relay"), ct);
                    await Assert.That(await http.GetStringAsync($"http://{target.Hostname}:{hostPort}/", ct)).IsEqualTo("linux-target");
                    using (var scope = provider.CreateScope())
                        await scope.ServiceProvider.GetRequiredService<NoCtfDbContext>().Teams.ExecuteUpdateAsync(update => update.SetProperty(team => team.IsBanned, false), ct);
                    first.Signal(); await ReadyAsync(statuses, runtimeId, ct);
                    var currentRelay = (await ResourcesAsync(docker, connector, ct)).Single(item => item.Labels["noctf.io/gateway-role"] == "relay");
                    await Assert.That(currentRelay.ID).IsNotEqualTo(oldRelay.ID);
                    var vanishedClient = (await ResourcesAsync(docker, connector, ct)).Single(item => item.Labels["noctf.io/gateway-role"] == "client");
                    await docker.Containers.RemoveContainerAsync(vanishedClient.ID, new ContainerRemoveParameters { Force = true }, ct);
                    first.Signal();
                    await WaitAsync(async () => (await ResourcesAsync(docker, connector, ct)).Any(item => item.Labels["noctf.io/gateway-role"] == "client" && item.ID != vanishedClient.ID), ct);
                    await ReadyAsync(statuses, runtimeId, ct);
                    currentRelay = (await ResourcesAsync(docker, connector, ct)).Single(item => item.Labels["noctf.io/gateway-role"] == "relay");
                    var oldPublication = new GatewayPublication(Guid.Parse(currentRelay.Labels["noctf.io/publication-id"]), runtimeId, target.Id, currentRelay.ID,
                        [new(null, 8080, hostPort)]);

                    using var secondTransport = new DockerSharedSshGateway(new("unix:///var/run/docker.sock"), options, TimeProvider.System);
                    using var second = new PublicGatewayAgent(provider.GetRequiredService<IServiceScopeFactory>(), secondTransport, capability, statuses, redis,
                        TimeProvider.System, logs.CreateLogger<PublicGatewayAgent>());
                    await second.StartAsync(ct);
                    try
                    {
                        await Task.Delay(1000, ct);
                        await Assert.That((await ResourcesAsync(docker, connector, ct)).Count(item => item.Labels["noctf.io/gateway-role"] == "client")).IsEqualTo(1);
                        // Simulate process loss: do not let the old agent clean its Docker resources.
                        // The successor must remove the orphaned master and relays during takeover.
                        slow.SuppressShutdownCleanup = true;
                        await first.StopAsync(ct);
                        await WaitAsync(async () => (await ResourcesAsync(docker, connector, ct)).Any(item => item.Labels["noctf.io/gateway-role"] == "relay" && item.ID != currentRelay.ID), ct);
                        ready = await ReadyAsync(statuses, runtimeId, ct);
                        address = new Uri($"http://{server.Hostname}:{server.GetMappedPublicPort(ready.Endpoints.Single().PublicPort!.Value)}/");
                        await firstTransport.RevokeAsync(oldPublication, ct);
                        await Assert.That(await http.GetStringAsync(address, ct)).IsEqualTo("linux-target");
                        using (var scope = provider.CreateScope())
                            await new PublicGatewayPolicyStore(scope.ServiceProvider.GetRequiredService<NoCtfDbContext>(), provider.GetRequiredService<IFusionCacheProvider>(),
                                new NoOpTransactionalMessageOutbox(), capability).SaveAsync(policy with { Enabled = false }, DateTimeOffset.UtcNow, ct);
                        second.Signal();
                        await WaitAsync(async () => !(await ResourcesAsync(docker, connector, ct)).Any(item => item.Labels["noctf.io/gateway-role"] == "relay"), ct);
                        await Assert.That(await http.GetStringAsync($"http://{target.Hostname}:{hostPort}/", ct)).IsEqualTo("linux-target");
                    }
                    finally { await second.StopAsync(ct); }
                }
                catch
                {
                    // Only dedicated per-test SSH/relay stderr; never dump config or private keys.
                    foreach (var resource in await ResourcesAsync(docker, connector, ct))
                    {
                        using var output = await docker.Containers.GetContainerLogsAsync(resource.ID,
                            new ContainerLogsParameters { ShowStderr = true, Tail = "8" }, ct);
                        var (_, error) = await output.ReadOutputToEndAsync(ct);
                        Console.WriteLine($"Fixture {resource.Labels["noctf.io/gateway-role"]}: {error}");
                    }
                    throw;
                }
                finally
                {
                    await first.StopAsync(ct);
                    // Fixture safety cleanup by a unique per-test connector, even after an assertion fails.
                    foreach (var resource in await ResourcesAsync(docker, connector, ct))
                        await docker.Containers.RemoveContainerAsync(resource.ID, new ContainerRemoveParameters { Force = true }, ct);
                }
                Console.WriteLine("Actual Linux Runner: real bind permissions, independent renewal, ban/unban, missing-client recovery, orphan takeover and disable PASS.");
            }
            finally { privateDirectory.Delete(recursive: true); }
        });
    }

    private static async Task SeedAsync(ServiceProvider provider, PublicGatewayCapability capability, PublicGatewayPolicy policy, Guid runtimeId, string targetId, int port, CancellationToken ct)
    {
        using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        await db.Database.EnsureCreatedAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid(); var competitionId = Guid.NewGuid(); var challengeId = Guid.NewGuid(); var instanceId = Guid.NewGuid(); var teamId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, UserName = "ssh-lab", NormalizedUserName = "SSH-LAB", Email = "ssh@example.test", PasswordHash = "test", CreatedAt = now, UpdatedAt = now });
        db.Competitions.Add(new Competition { Id = competitionId, Title = "Isolated SSH lab", OwnerId = userId, Mode = GameMode.Ctf, Status = CompetitionStatus.Running,
            ConfigurationJson = "{}", FlagDerivationSecret = new byte[32], StartAt = now.AddMinutes(-1), EndAt = now.AddHours(1), CreatedAt = now, UpdatedAt = now });
        var definition = new CtfChallengeConfiguration(CtfChallengeConfiguration.CurrentSchemaVersion, null, null,
            Runtime: new(RuntimeAllocation.PerTeam, new ContainerRuntimeDefinition(PublicGatewaySafetyPrototypeTests.Image), UrlBindings: [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 8080)]));
        db.Challenges.Add(new Challenge { Id = challengeId, OwnerId = userId, Mode = GameMode.Ctf, Title = "SSH target", Direction = "Web",
            DefinitionJson = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)), CreatedAt = now, UpdatedAt = now });
        db.CompetitionChallenges.Add(new CompetitionChallenge { Id = instanceId, CompetitionId = competitionId, ChallengeId = challengeId, IsPublished = true, RulesJson = "{}", UpdatedAt = now });
        db.Teams.Add(new Team { Id = teamId, CompetitionId = competitionId, CaptainId = userId, MemberIds = [userId], Name = "Lab", InvitationToken = new string('s', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = now });
        db.RuntimeInstances.Add(new RuntimeInstance { Id = runtimeId, CompetitionId = competitionId, CompetitionChallengeId = instanceId, TeamId = teamId,
            Purpose = RuntimePurpose.Player, RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker, RunnerId = "test-runner", State = RuntimeState.Running,
            CreatedAt = now, RunningAt = now, ExpiresAt = now.AddMinutes(10), PublishedPorts = [new() { ContainerPort = 8080, HostPort = port }],
            ProviderReceiptJson = JsonSerializer.Serialize(new ContainerReceipt(runtimeId, RuntimeProvider.Docker, targetId, RuntimeStatus.Running,
                new Dictionary<int, int> { [8080] = port }, "direct", "target", RuntimeInstanceId: runtimeId)) });
        await db.SaveChangesAsync(ct);
        await new PublicGatewayPolicyStore(db, provider.GetRequiredService<IFusionCacheProvider>(), new NoOpTransactionalMessageOutbox(), capability).SaveAsync(policy, now, ct);
    }
    private static async Task<PublicRuntimeStatus> ReadyAsync(IPublicGatewayStatusStore statuses, Guid runtimeId, CancellationToken ct)
    {
        PublicRuntimeStatus? result = null;
        await WaitAsync(async () => (result = await statuses.GetAsync(runtimeId, ct))?.Endpoints.Any(item => item.State == PublicAccessState.Ready) == true, ct);
        return result!;
    }
    private static async Task WaitAsync(Func<Task<bool>> predicate, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 120; attempt++) { if (await predicate()) return; await Task.Delay(250, ct); }
        throw new InvalidOperationException("Linux gateway lifecycle did not converge within 30 seconds.");
    }
    private static Task<IList<ContainerListResponse>> ResourcesAsync(DockerClient docker, string connector, CancellationToken ct) => docker.Containers.ListContainersAsync(new ContainersListParameters
    { All = true, Filters = new Dictionary<string, IDictionary<string, bool>> { ["label"] = new Dictionary<string, bool> { [$"noctf.io/public-gateway-connector={connector}"] = true } } }, ct);
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static (byte[] Private, string Public) Key()
    {
        using var rsa = RSA.Create(2048); var values = rsa.ExportParameters(false);
        using var data = new MemoryStream();
        static void Field(Stream target, byte[] value, bool integer = false)
        {
            var pad = integer && (value[0] & 128) != 0; Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, value.Length + (pad ? 1 : 0)); target.Write(length);
            if (pad) target.WriteByte(0); target.Write(value);
        }
        Field(data, Bytes("ssh-rsa")); Field(data, values.Exponent!, true); Field(data, values.Modulus!, true);
        return (Bytes(rsa.ExportRSAPrivateKeyPem()), "ssh-rsa " + Convert.ToBase64String(data.ToArray()) + " isolated-fixture\n");
    }
    private sealed class SlowTransport(IPublicGatewayTransport inner) : IPublicGatewayTransport
    {
        public bool UsesIndependentPublicPorts => true;
        public bool RequiresReset => inner.RequiresReset;
        public bool DelayNext;
        public bool SuppressShutdownCleanup;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<GatewayPublication> StartAsync(Guid id, string target, IReadOnlyList<RuntimePublishedPortView> ports, CancellationToken ct) => inner.StartAsync(id, target, ports, ct);
        public Task RenewAsync(GatewayPublication publication, DateTimeOffset expires, CancellationToken ct) => inner.RenewAsync(publication, expires, ct);
        public async Task<IReadOnlyList<PublicEndpointStatus>> ReadStatusAsync(GatewayPublication publication, CancellationToken ct)
        {
            if (DelayNext) { DelayNext = false; Entered.TrySetResult(); await Task.Delay(12_000, ct); Finished.TrySetResult(); }
            return await inner.ReadStatusAsync(publication, ct);
        }
        public Task RevokeAsync(GatewayPublication publication, CancellationToken ct) => SuppressShutdownCleanup ? Task.CompletedTask : inner.RevokeAsync(publication, ct);
        public Task RevokeStaleHelpersAsync(CancellationToken ct) => inner.RevokeStaleHelpersAsync(ct);
        public Task StopAsync(CancellationToken ct) => SuppressShutdownCleanup ? Task.CompletedTask : inner.StopAsync(ct);
    }
    private const string TargetScript = """
        from http.server import BaseHTTPRequestHandler, HTTPServer
        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                self.send_response(200); self.end_headers(); self.wfile.write(b'linux-target')
        HTTPServer(('0.0.0.0',8080),Handler).serve_forever()
        """;
    private const string ServerConfig = """
        Port 60999
        ListenAddress 0.0.0.0
        HostKey /config/host_key
        AuthorizedKeysFile /config/authorized_keys
        AllowUsers noctf
        PasswordAuthentication no
        KbdInteractiveAuthentication no
        PubkeyAuthentication yes
        PermitRootLogin no
        PermitEmptyPasswords no
        AllowTcpForwarding remote
        AllowStreamLocalForwarding no
        GatewayPorts clientspecified
        PermitListen 0.0.0.0:40000 0.0.0.0:40001
        PermitOpen none
        MaxSessions 0
        PermitTTY no
        AllowAgentForwarding no
        X11Forwarding no
        ForceCommand /bin/false
        ClientAliveInterval 3
        ClientAliveCountMax 3
        LogLevel ERROR
        """;
}

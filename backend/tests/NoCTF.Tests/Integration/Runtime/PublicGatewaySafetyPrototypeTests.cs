using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Docker.DotNet;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Docker.PublicAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.PublicAccess;
using NoCTF.Runner.PublicAccess;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Runtime;

/// <summary>
/// Stage A negative controls, NOT a production gateway implementation.
/// A passing negative control proves the candidate is unsafe for unmanaged Docker removal.
/// </summary>
[Category("Integration"), Category("GatewaySafetyPrototype"), NotInParallel]
public sealed class PublicGatewaySafetyPrototypeTests
{
    internal const string Image = "m.daocloud.io/docker.io/library/python:3.13-alpine@sha256:540c7d91f98ff6880174c40e99067bf5941eb54d818a7a5e094d188b196a934d";
    private const string ArchiveHash = "3CF934477F4FB1EE9E19E49C31FB33F5FFE3283300076F59AFAD8B8CCF1E1621";
    internal static readonly Lazy<Task<Dictionary<string, byte[]>>> Binaries = new(ReadBinariesAsync);

    [Test, Timeout(180_000)]
    public async Task Actual_agent_reconciles_postgres_facts_and_disables_without_stopping_the_target(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            var binaries = await Binaries.Value.WaitAsync(ct);
            var tls = CreateCertificates();
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var runtimeId = Guid.NewGuid();
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var redisContainer = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(postgres.StartAsync(ct), redisContainer.StartAsync(ct));
            await using var redis = await ConnectionMultiplexer.ConnectAsync(redisContainer.GetConnectionString());
            await using var network = new NetworkBuilder().Build(); await network.CreateAsync(ct);
            await using var target = Target("agent-A").WithNetwork(network).WithLabel("noctf.io/runtime-instance-id", runtimeId.ToString())
                .WithPortBinding(8080, true).Build();
            await target.StartAsync(ct);
            var hostPort = target.GetMappedPublicPort(8080);
            await using var server = new ContainerBuilder(Image).WithNetwork(network).WithNetworkAliases("frps")
                .WithPortBinding(hostPort, true).WithResourceMapping(binaries["frps"], "/frps")
                .WithResourceMapping(tls.Ca, "/ca.crt").WithResourceMapping(tls.ServerCert, "/server.crt").WithResourceMapping(tls.ServerKey, "/server.key")
                .WithResourceMapping(Bytes($$"""
                    bindPort = 7000
                    allowPorts = [{ single = {{hostPort}} }]
                    auth.token = "{{token}}"
                    transport.tls.force = true
                    transport.tls.certFile = "/server.crt"
                    transport.tls.keyFile = "/server.key"
                    transport.tls.trustedCaFile = "/ca.crt"
                    """), "/frps.toml")
                .WithEntrypoint("/bin/sh", "-c", "chmod 700 /frps && exec /frps -c /frps.toml")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(7000)).Build();
            await server.StartAsync(ct);
            var credentials = Directory.CreateTempSubdirectory("noctf-gateway-agent-test-");
            try
            {
                await File.WriteAllBytesAsync(Path.Combine(credentials.FullName, "ca.crt"), tls.Ca, ct);
                await File.WriteAllBytesAsync(Path.Combine(credentials.FullName, "client.crt"), tls.ClientCert, ct);
                await File.WriteAllBytesAsync(Path.Combine(credentials.FullName, "client.key"), tls.ClientKey, ct);
                await File.WriteAllTextAsync(Path.Combine(credentials.FullName, "token"), token, ct);
                var connector = "test-" + Guid.NewGuid().ToString("N");
                var capability = new PublicGatewayCapability(connector, "test-runner", ["https://public.example.test"], hostPort, hostPort, [], 1, true);
                using var adapter = new DockerPublicGateway(new DockerRuntimeOptions(Environment.GetEnvironmentVariable("DOCKER_HOST")
                    ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock")), new(connector, "test-runner",
                    Environment.GetEnvironmentVariable("NOCTF_GATEWAY_IMAGE") ?? "noctf-public-gateway:local", "frps", 7000, "frps",
                    Path.Combine(credentials.FullName, "ca.crt"), Path.Combine(credentials.FullName, "client.crt"), Path.Combine(credentials.FullName, "client.key"), Path.Combine(credentials.FullName, "token")));
                var services = new ServiceCollection();
                services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
                services.AddSingleton(TimeProvider.System);
                services.AddScoped<PublicGatewayLeaseGuard>();
                services.AddFusionCache(NoCtfCacheNames.ReadModels);
                using var provider = services.BuildServiceProvider();
                var statuses = new PublicGatewayStatusStore(provider.GetRequiredService<IFusionCacheProvider>(), TimeProvider.System);
                var policy = new PublicGatewayPolicy(true, connector, capability.ApprovedOrigins[0], ["https://direct.example.test"], "203.0.113.1", null, 1);
                using (var scope = provider.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    await db.Database.EnsureCreatedAsync(ct);
                    var now = DateTimeOffset.UtcNow;
                    var userId = Guid.NewGuid(); var competitionId = Guid.NewGuid(); var challengeId = Guid.NewGuid(); var instanceId = Guid.NewGuid(); var teamId = Guid.NewGuid();
                    db.Users.Add(new User { Id = userId, UserName = "gateway-lab", NormalizedUserName = "GATEWAY-LAB", Email = "gateway@example.test", PasswordHash = "test", CreatedAt = now, UpdatedAt = now });
                    db.Competitions.Add(new Competition { Id = competitionId, Title = "Gateway lab", OwnerId = userId, Mode = GameMode.Ctf, Status = CompetitionStatus.Running,
                        ConfigurationJson = "{}", FlagDerivationSecret = new byte[32], StartAt = now.AddMinutes(-1), EndAt = now.AddHours(1), CreatedAt = now, UpdatedAt = now });
                    var definition = new CtfChallengeConfiguration(CtfChallengeConfiguration.CurrentSchemaVersion, null, null,
                        Runtime: new(RuntimeAllocation.PerTeam, new ContainerRuntimeDefinition(Image), UrlBindings: [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 8080)]));
                    db.Challenges.Add(new Challenge { Id = challengeId, OwnerId = userId, Mode = GameMode.Ctf, Title = "Gateway target", Direction = "Web",
                        DefinitionJson = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)), CreatedAt = now, UpdatedAt = now });
                    db.CompetitionChallenges.Add(new CompetitionChallenge { Id = instanceId, CompetitionId = competitionId, ChallengeId = challengeId, IsPublished = true, RulesJson = "{}", UpdatedAt = now });
                    db.Teams.Add(new Team { Id = teamId, CompetitionId = competitionId, CaptainId = userId, MemberIds = [userId], Name = "Gateway team", InvitationToken = new string('g', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = now });
                    db.RuntimeInstances.Add(new RuntimeInstance { Id = runtimeId, CompetitionId = competitionId, CompetitionChallengeId = instanceId, TeamId = teamId,
                        Purpose = RuntimePurpose.Player, RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker, RunnerId = "test-runner", State = RuntimeState.Running,
                        CreatedAt = now, RunningAt = now, ExpiresAt = now.AddMinutes(10), PublishedPorts = [new() { ContainerPort = 8080, HostPort = hostPort }],
                        ProviderReceiptJson = JsonSerializer.Serialize(new ContainerReceipt(runtimeId, RuntimeProvider.Docker, target.Id, RuntimeStatus.Running, new Dictionary<int, int> { [8080] = hostPort }, "direct", "target", RuntimeInstanceId: runtimeId)) });
                    await db.SaveChangesAsync(ct);
                    await new PublicGatewayPolicyStore(db, provider.GetRequiredService<IFusionCacheProvider>(), new NoOpTransactionalMessageOutbox(), capability).SaveAsync(policy, now, ct);
                }
                using var agent = new PublicGatewayAgent(provider.GetRequiredService<IServiceScopeFactory>(), adapter, capability, statuses, redis, TimeProvider.System, NullLogger<PublicGatewayAgent>.Instance);
                await agent.StartAsync(ct);
                try
                {
                    var ready = false;
                    for (var attempt = 0; attempt < 60; attempt++)
                    {
                        if ((await statuses.GetAsync(runtimeId, ct))?.Endpoints.Any(item => item.State == NoCTF.Domain.Platform.PublicAccessState.Ready) == true) { ready = true; break; }
                        await Task.Delay(250, ct);
                    }
                    await Assert.That(ready).IsTrue();
                    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
                    await ExpectBodyAsync(http, new Uri($"http://{server.Hostname}:{server.GetMappedPublicPort(hostPort)}/"), "agent-A", ct);
                    using (var scope = provider.CreateScope())
                        await new PublicGatewayPolicyStore(scope.ServiceProvider.GetRequiredService<NoCtfDbContext>(), provider.GetRequiredService<IFusionCacheProvider>(), new NoOpTransactionalMessageOutbox(), capability)
                            .SaveAsync(policy with { Enabled = false }, DateTimeOffset.UtcNow, ct);
                    agent.Signal();
                    var disabled = false;
                    for (var attempt = 0; attempt < 40; attempt++)
                    {
                        var status = await statuses.GetConnectorAsync(connector, ct);
                        if (status?.PolicyHash == (policy with { Enabled = false }).Fingerprint() && status.Runtimes.Count == 0) { disabled = true; break; }
                        await Task.Delay(250, ct);
                    }
                    await Assert.That(disabled).IsTrue();
                    await Assert.That(await ReadBodyAsync(http, new Uri($"http://{server.Hostname}:{server.GetMappedPublicPort(hostPort)}/"), ct)).IsNull();
                    await ExpectBodyAsync(http, new Uri($"http://{target.Hostname}:{hostPort}/"), "agent-A", ct);
                }
                finally { await agent.StopAsync(ct); }
            }
            finally { credentials.Delete(recursive: true); }
        });
    }

    [Test, Timeout(120_000)]
    public async Task Actual_adapter_verifies_binding_renews_atomically_and_revokes_without_remote_ack(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            var binaries = await Binaries.Value.WaitAsync(ct);
            var tls = CreateCertificates();
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var runtimeId = Guid.NewGuid();
            await using var network = new NetworkBuilder().Build(); await network.CreateAsync(ct);
            await using var target = Target("adapter-A").WithNetwork(network)
                .WithLabel("noctf.io/runtime-instance-id", runtimeId.ToString()).WithPortBinding(8080, true).Build();
            await target.StartAsync(ct);
            var hostPort = target.GetMappedPublicPort(8080);
            await using var server = new ContainerBuilder(Image).WithNetwork(network).WithNetworkAliases("frps")
                .WithPortBinding(hostPort, true).WithResourceMapping(binaries["frps"], "/frps")
                .WithResourceMapping(tls.Ca, "/ca.crt").WithResourceMapping(tls.ServerCert, "/server.crt").WithResourceMapping(tls.ServerKey, "/server.key")
                .WithResourceMapping(Bytes($$"""
                    bindPort = 7000
                    allowPorts = [{ single = {{hostPort}} }]
                    auth.token = "{{token}}"
                    transport.tls.force = true
                    transport.tls.certFile = "/server.crt"
                    transport.tls.keyFile = "/server.key"
                    transport.tls.trustedCaFile = "/ca.crt"
                    """), "/frps.toml")
                .WithEntrypoint("/bin/sh", "-c", "chmod 700 /frps && exec /frps -c /frps.toml")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(7000)).Build();
            await server.StartAsync(ct);
            var credentials = Directory.CreateTempSubdirectory("noctf-gateway-adapter-test-");
            try
            {
                await File.WriteAllBytesAsync(Path.Combine(credentials.FullName, "ca.crt"), tls.Ca, ct);
                await File.WriteAllBytesAsync(Path.Combine(credentials.FullName, "client.crt"), tls.ClientCert, ct);
                await File.WriteAllBytesAsync(Path.Combine(credentials.FullName, "client.key"), tls.ClientKey, ct);
                await File.WriteAllTextAsync(Path.Combine(credentials.FullName, "token"), token, ct);
                using var adapter = new DockerPublicGateway(new DockerRuntimeOptions(
                    Environment.GetEnvironmentVariable("DOCKER_HOST") ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock")),
                    new("test-" + Guid.NewGuid().ToString("N"), "test-runner",
                        Environment.GetEnvironmentVariable("NOCTF_GATEWAY_IMAGE") ?? "noctf-public-gateway:local", "frps", 7000, "frps",
                        Path.Combine(credentials.FullName, "ca.crt"), Path.Combine(credentials.FullName, "client.crt"),
                        Path.Combine(credentials.FullName, "client.key"), Path.Combine(credentials.FullName, "token")));
                try
                {
                    RuntimePublishedPortView[] ports = [new(null, 8080, hostPort)];
                    await Assert.That(await adapter.VerifyTargetAsync(Guid.NewGuid(), target.Id, ports, ct)).IsFalse();
                    var publication = await adapter.StartAsync(runtimeId, target.Id, ports, ct);
                    await adapter.RenewAsync(publication, DateTimeOffset.UtcNow.AddSeconds(9), ct);
                    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
                    var address = new Uri($"http://{server.Hostname}:{server.GetMappedPublicPort(hostPort)}/");
                    await ExpectBodyAsync(http, address, "adapter-A", ct);
                    await Assert.That(await adapter.ReadyPortsAsync(publication, ct)).Contains(8080);
                    for (var index = 0; index < 3; index++)
                    {
                        await adapter.RenewAsync(publication, DateTimeOffset.UtcNow.AddSeconds(9), ct);
                        await ExpectBodyAsync(http, address, "adapter-A", ct);
                    }
                    await server.StopAsync(ct);
                    using var local = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await adapter.RevokeAsync(publication, local.Token);
                    await adapter.RevokeAsync(publication, ct);
                    await ExpectBodyAsync(http, new Uri($"http://{target.Hostname}:{hostPort}/"), "adapter-A", ct);
                }
                finally
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await adapter.RevokeStaleHelpersAsync(cleanup.Token);
                }
            }
            finally { credentials.Delete(recursive: true); }
        });
    }

    [Test, Arguments(false, false, false, false), Arguments(true, false, false, false), Arguments(false, true, false, false), Arguments(true, true, false, false), Arguments(false, false, true, false), Arguments(false, false, true, true)]
    [Timeout(120_000)]
    public async Task Port_reuse_negative_control_and_explicit_revoke_boundary(
        bool unixSocket, bool revokeFirst, bool namespaceBound, bool leased, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            var binaries = await Binaries.Value.WaitAsync(ct);
            var tls = CreateCertificates();
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await using var network = new NetworkBuilder().Build();
            await network.CreateAsync(ct);
            await using var targetA = Target("runtime-A").WithNetwork(network).WithPortBinding(8080, true).Build();
            await targetA.StartAsync(ct);
            var localPort = targetA.GetMappedPublicPort(8080);
            await using var server = new ContainerBuilder(Image).WithNetwork(network).WithNetworkAliases("frps")
                .WithPortBinding(31001, true)
                .WithResourceMapping(binaries["frps"], "/frps")
                .WithResourceMapping(tls.Ca, "/ca.crt")
                .WithResourceMapping(tls.ServerCert, "/server.crt")
                .WithResourceMapping(tls.ServerKey, "/server.key")
                .WithResourceMapping(Bytes($$"""
                    bindPort = 7000
                    allowPorts = [{ single = 31001 }]
                    maxPortsPerClient = 1
                    auth.method = "token"
                    auth.token = "{{token}}"
                    transport.tls.force = true
                    transport.tls.certFile = "/server.crt"
                    transport.tls.keyFile = "/server.key"
                    transport.tls.trustedCaFile = "/ca.crt"
                    """), "/frps.toml")
                .WithEntrypoint("/bin/sh", "-c", "chmod 700 /frps && exec /frps -c /frps.toml")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(7000))
                .Build();
            await server.StartAsync(ct);
            var proxy = unixSocket
                ? """
                  [proxies.plugin]
                  type = "unix_domain_socket"
                  unixPath = "/runtime-A.sock"
                  """
                : namespaceBound ? "localIP = \"127.0.0.1\"\nlocalPort = 8080"
                : $"localIP = \"host.docker.internal\"\nlocalPort = {localPort}";
            var clientBuilder = namespaceBound
                ? new ContainerBuilder(Image).WithCreateParameterModifier(parameters =>
                {
                    parameters.HostConfig!.NetworkMode = $"container:{targetA.Id}";
                    parameters.HostConfig.Memory = 32 * 1024 * 1024;
                    parameters.HostConfig.NanoCPUs = 100_000_000;
                })
                : new ContainerBuilder(Image).WithNetwork(network).WithExtraHost("host.docker.internal", "host-gateway");
            var publicationId = Guid.NewGuid().ToString();
            var clientConfigPath = leased ? "/run/noctf-gateway/frpc.toml" : "/frpc.toml";
            if (leased)
            {
                clientBuilder = clientBuilder
                    .WithEnvironment("NOCTF_PUBLICATION_ID", publicationId)
                    .WithResourceMapping(await File.ReadAllBytesAsync(RepositoryFile("deploy/public-gateway/lease_guard.py"), ct), "/lease_guard.py")
                    .WithResourceMapping(Bytes(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        publicationId,
                        expiresAtUnixMs = DateTimeOffset.UtcNow.AddSeconds(9).ToUnixTimeMilliseconds()
                    })), "/run/noctf-gateway/lease.json");
            }
            await using var client = clientBuilder
                .WithResourceMapping(binaries["frpc"], "/frpc")
                .WithResourceMapping(tls.Ca, "/ca.crt")
                .WithResourceMapping(tls.ClientCert, "/client.crt")
                .WithResourceMapping(tls.ClientKey, "/client.key")
                .WithResourceMapping(Bytes(SocketForwarder), "/gate.py")
                .WithEnvironment("UPSTREAM_PORT", localPort.ToString(CultureInfo.InvariantCulture))
                .WithResourceMapping(Bytes($$"""
                    serverAddr = "frps"
                    serverPort = 7000
                    auth.method = "token"
                    auth.token = "{{token}}"
                    transport.tls.enable = true
                    transport.tls.serverName = "frps"
                    transport.tls.trustedCaFile = "/ca.crt"
                    transport.tls.certFile = "/client.crt"
                    transport.tls.keyFile = "/client.key"
                    [[proxies]]
                    name = "runtime-A"
                    type = "tcp"
                    remotePort = 31001
                    {{proxy}}
                    """), clientConfigPath)
                .WithEntrypoint("/bin/sh", "-c", leased
                    ? "chmod 700 /frpc && ln -s /frpc /usr/local/bin/frpc && exec python /lease_guard.py"
                    : unixSocket
                    ? "chmod 700 /frpc && python /gate.py & wait"
                    : "chmod 700 /frpc && exec /frpc -c /frpc.toml")
                .Build();
            await client.StartAsync(ct);
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
            {
                Timeout = TimeSpan.FromSeconds(2)
            };
            var address = new Uri($"http://{server.Hostname}:{server.GetMappedPublicPort(31001)}/");
            await ExpectBodyAsync(http, address, "runtime-A", ct);
            if (leased)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                await Assert.That(await ReadBodyAsync(http, address, ct)).IsNull();
                await ExpectBodyAsync(http, new Uri($"http://{targetA.Hostname}:{localPort}/"), "runtime-A", ct);
                Console.WriteLine("LEASE CANDIDATE PASS: no renewals revoke public access while direct runtime-A remains healthy.");
                return;
            }
            string? originalNamespace = null;
            if (namespaceBound)
            {
                originalNamespace = (await targetA.ExecAsync(["readlink", "/proc/self/ns/net"], ct)).Stdout.Trim();
                await Assert.That((await client.ExecAsync(["readlink", "/proc/self/ns/net"], ct)).Stdout.Trim())
                    .IsEqualTo(originalNamespace);
            }
            if (revokeFirst)
                await client.StopAsync(ct);

            // Only this test-owned resource is removed. B deliberately takes A's released host port.
            await targetA.DisposeAsync();
            await using var targetB = Target("runtime-B").WithPortBinding(localPort, 8080).Build();
            await targetB.StartAsync(ct);
            if (namespaceBound)
            {
                await Assert.That((await targetB.ExecAsync(["readlink", "/proc/self/ns/net"], ct)).Stdout.Trim())
                    .IsNotEqualTo(originalNamespace);
                await Assert.That(await ReadBodyAsync(http, address, ct)).IsNull();
                await server.StopAsync(ct);
                await server.StartAsync(ct);
                await Assert.That(await ReadBodyAsync(http, address, ct)).IsNull();
                await client.StopAsync(ct);
                try { await client.StartAsync(ct); }
                catch (DockerApiException) { /* A removed namespace owner cannot be joined on restart. */ }
                await Assert.That(await ReadBodyAsync(http, address, ct)).IsNull();
                Console.WriteLine("NAMESPACE CANDIDATE PASS: stale runtime-A cannot dial runtime-B after unmanaged removal/port reuse and frps restart.");
            }
            else if (revokeFirst)
            {
                await Assert.That(await ReadBodyAsync(http, address, ct)).IsNull();
                Console.WriteLine($"GATE CONTROL PASS: unix={unixSocket}; local data plane stopped before port reuse.");
            }
            else
            {
                await ExpectBodyAsync(http, address, "runtime-B", ct);
                // Restarting with stale configuration also restores the unsafe route.
                await client.StopAsync(ct);
                await client.StartAsync(ct);
                await ExpectBodyAsync(http, address, "runtime-B", ct);
                Console.WriteLine($"GATE BLOCKED: unix={unixSocket}; stale runtime-A publication reached runtime-B, including after frpc restart.");
            }
        });
    }

    private static ContainerBuilder Target(string body) => new ContainerBuilder(Image)
        .WithResourceMapping(Bytes(body), "/www/index.html")
        .WithEntrypoint("python", "-m", "http.server", "8080", "--directory", "/www")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080));

    private static async Task<string?> ReadBodyAsync(HttpClient http, Uri address, CancellationToken ct)
    {
        try { return await http.GetStringAsync(address, ct); }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }

    private static async Task ExpectBodyAsync(HttpClient http, Uri address, string expected, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (await ReadBodyAsync(http, address, ct) == expected) return;
            await Task.Delay(200, ct);
        }
        throw new InvalidOperationException($"Isolated FRP probe did not reach {expected}.");
    }

    private static async Task<Dictionary<string, byte[]>> ReadBinariesAsync()
    {
        // Offline-friendly: set this path to the unmodified official release archive.
        var path = Environment.GetEnvironmentVariable("NOCTF_FRP_ARCHIVE");
        byte[] archive;
        if (!string.IsNullOrWhiteSpace(path)) archive = await File.ReadAllBytesAsync(path);
        else
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            archive = await http.GetByteArrayAsync("https://github.com/fatedier/frp/releases/download/v0.68.0/frp_0.68.0_linux_amd64.tar.gz");
        }
        if (Convert.ToHexString(SHA256.HashData(archive)) != ArchiveHash)
            throw new InvalidDataException("FRP release archive SHA-256 mismatch.");
        using var gzip = new GZipStream(new MemoryStream(archive), CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var files = new Dictionary<string, byte[]>();
        while (await tar.GetNextEntryAsync() is { } entry)
        {
            if (entry.Name is not ("frp_0.68.0_linux_amd64/frpc" or "frp_0.68.0_linux_amd64/frps")) continue;
            using var output = new MemoryStream();
            await entry.DataStream!.CopyToAsync(output);
            files.Add(Path.GetFileName(entry.Name), output.ToArray());
        }
        if (files.Count != 2) throw new InvalidDataException("FRP archive does not contain both binaries.");
        return files;
    }

    internal static (byte[] Ca, byte[] ServerCert, byte[] ServerKey, byte[] ClientCert, byte[] ClientKey) CreateCertificates()
    {
        using var caKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=NoCTF isolated test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        (byte[] Cert, byte[] Key) Leaf(string name, string usage)
        {
            using var key = RSA.Create(2048);
            var leaf = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(name);
            leaf.CertificateExtensions.Add(san.Build());
            leaf.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            leaf.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(usage) }, true));
            using var cert = leaf.Create(ca, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1), RandomNumberGenerator.GetBytes(16));
            return (Bytes(cert.ExportCertificatePem()), Bytes(key.ExportPkcs8PrivateKeyPem()));
        }
        var server = Leaf("frps", "1.3.6.1.5.5.7.3.1");
        var client = Leaf("frpc", "1.3.6.1.5.5.7.3.2");
        return (Bytes(ca.ExportCertificatePem()), server.Cert, server.Key, client.Cert, client.Key);
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static string RepositoryFile(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Gateway prototype source was not found.", relative);
    }

    // A unique socket path alone provides no lifetime binding to the upstream container.
    private const string SocketForwarder = """
        import asyncio, os
        async def client(reader, writer):
            upstream = None
            try:
                other, upstream = await asyncio.open_connection('host.docker.internal', int(os.environ['UPSTREAM_PORT']))
                async def copy(source, destination):
                    while data := await source.read(16384):
                        destination.write(data)
                        await destination.drain()
                    destination.close()
                await asyncio.gather(copy(reader, upstream), copy(other, writer))
            except (OSError, ConnectionError):
                pass
            finally:
                writer.close()
                if upstream: upstream.close()
        async def main():
            if os.path.exists('/runtime-A.sock'): os.unlink('/runtime-A.sock')
            server = await asyncio.start_unix_server(client, path='/runtime-A.sock')
            frpc = await asyncio.create_subprocess_exec('/frpc', '-c', '/frpc.toml')
            async with server:
                await frpc.wait()
        asyncio.run(main())
        """;
}

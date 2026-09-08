using System.Net.Sockets;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), Category("GatewaySafetyPrototype"), NotInParallel]
public sealed class SharedTunnelRelayPrototypeTests
{
    [Test, Timeout(180_000)]
    public Task Original_frp_negative_control_loses_half_close(CancellationToken ct) => ExerciseAsync(false, ct);

    [Test, Timeout(180_000)]
    public Task OpenSsh_preserves_web_half_close_and_instance_isolation(CancellationToken ct) => ExerciseAsync(true, ct);

    [Test, Timeout(30_000)]
    public async Task Relay_rejects_invalid_lease_files_without_blocking(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            var relayFile = Environment.GetEnvironmentVariable("NOCTF_RELAY_BINARY")
                ?? throw new InvalidOperationException("Build the Linux relay and set NOCTF_RELAY_BINARY.");
            await using var fixture = new ContainerBuilder(PublicGatewaySafetyPrototypeTests.Image)
                .WithResourceMapping(await File.ReadAllBytesAsync(relayFile, ct), "/relay", 0, 0, UnixFileModes.UserRead | UnixFileModes.UserExecute)
                .WithEntrypoint("sleep", "infinity")
                .Build();
            await fixture.StartAsync(ct);
            var result = await fixture.ExecAsync(["python", "-c", """
                import json, os, pathlib, subprocess, tempfile, time, uuid
                identity = str(uuid.uuid4())
                with tempfile.TemporaryDirectory(prefix='lease-') as directory:
                    base = pathlib.Path(directory)
                    for case in ('fifo', 'symlink', 'oversized', 'directory', 'missing', 'malformed', 'expired', 'future', 'wrong-identity'):
                        root = base / case
                        root.mkdir()
                        target = root / 'lease.json'
                        valid = json.dumps({'publicationId': identity, 'expiresAtUnixMs': int(time.time()*1000)+5000})
                        if case == 'fifo': os.mkfifo(target)
                        elif case == 'symlink':
                            (root / 'other').write_text(valid)
                            target.symlink_to(root / 'other')
                        elif case == 'directory': target.mkdir()
                        elif case == 'oversized': target.write_text(' ' * 513)
                        elif case == 'malformed': target.write_text('{')
                        elif case != 'missing':
                            target.write_text(json.dumps({'publicationId': str(uuid.uuid4()) if case == 'wrong-identity' else identity,
                                'expiresAtUnixMs': int(time.time()*1000) + (-1000 if case == 'expired' else 60000 if case == 'future' else 5000)}))
                        try:
                            process = subprocess.run(['/relay', 'run', str(root), str(root), identity, '1', '8080'],
                                capture_output=True, timeout=2)
                        except subprocess.TimeoutExpired:
                            raise AssertionError(case + ': lease validation blocked')
                        assert process.returncode == 1, (case, process.returncode)
                        assert not (root / 'started').exists(), case
                        print(case + ': rejected before publication')
                """], ct);
            Console.WriteLine(result.Stdout);
            await Assert.That(result.ExitCode).IsEqualTo(0L).Because(result.Stderr);
        });
    }

    private static async Task ExerciseAsync(bool ssh, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            var relayFile = Environment.GetEnvironmentVariable("NOCTF_RELAY_BINARY")
                ?? throw new InvalidOperationException("Build the Linux relay and set NOCTF_RELAY_BINARY.");
            var relayBinary = await File.ReadAllBytesAsync(relayFile, ct);
            var binaries = await PublicGatewaySafetyPrototypeTests.Binaries.Value.WaitAsync(ct);
            var tls = PublicGatewaySafetyPrototypeTests.CreateCertificates();
            var targetTls = PublicGatewaySafetyPrototypeTests.CreateCertificates();
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            using var sshKeys = ssh ? await EphemeralSshKeys.CreateAsync(ct) : null;
            var a = Guid.NewGuid().ToString();
            var b = Guid.NewGuid().ToString();
            await using var network = new NetworkBuilder().Build();
            await network.CreateAsync(ct);
            // Disposable Linux filesystem fixture (not a production deployment volume).
            await using var volume = new VolumeBuilder().Build();
            await volume.CreateAsync(ct);
            using var docker = new DockerClientBuilder().WithEndpoint(new Uri(Environment.GetEnvironmentVariable("DOCKER_HOST")
                ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock"))).Build();
            var mountpoint = (await docker.Volumes.InspectAsync(volume.Name, ct)).Mountpoint;
            await using var leases = new ContainerBuilder(PublicGatewaySafetyPrototypeTests.Image)
                .WithVolumeMount(volume, "/endpoints")
                .WithResourceMapping(Bytes(LeaseWriter), "/renew.py")
                .WithEntrypoint("python", "/renew.py", a, b)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilFileExists("/initialized", FileSystem.Container))
                .Build();
            await leases.StartAsync(ct);
            await using var targetA = Target("A").WithNetwork(network).WithPortBinding(8080, true).WithPortBinding(8081, true)
                .WithResourceMapping(targetTls.ServerCert, "/target.crt").WithResourceMapping(targetTls.ServerKey, "/target.key").Build();
            await using var targetB = Target("B").WithNetwork(network).WithPortBinding(8080, true).Build();
            await Task.WhenAll(targetA.StartAsync(ct), targetB.StartAsync(ct));
            var aPort = targetA.GetMappedPublicPort(8080);
            ContainerBuilder Relay(IContainer target, string id, params string[] ports) =>
                new ContainerBuilder(PublicGatewaySafetyPrototypeTests.Image)
                    .WithResourceMapping(relayBinary, "/relay", 65532, 65532, UnixFileModes.UserRead | UnixFileModes.UserExecute)
                    .WithEntrypoint(["/relay", "run", "/endpoint", "/lease", id, "32", .. ports])
                    .WithCreateParameterModifier(parameters =>
                    {
                        parameters.User = "65532:65532";
                        parameters.HostConfig!.NetworkMode = "container:" + target.Id;
                        parameters.HostConfig.Binds = [$"{mountpoint}/{id}/listen:/endpoint", $"{mountpoint}/{id}/control:/lease:ro"];
                        parameters.HostConfig.Memory = 16 * 1024 * 1024;
                        parameters.HostConfig.NanoCPUs = 50_000_000;
                        parameters.HostConfig.PidsLimit = 32;
                    });
            await using var relayA = Relay(targetA, a, "8080", "8081", "8082", "8083").Build();
            await using var relayB = Relay(targetB, b, "8080").Build();
            await Task.WhenAll(relayA.StartAsync(ct), relayB.StartAsync(ct));
            var frpServer = new ContainerBuilder(PublicGatewaySafetyPrototypeTests.Image)
                .WithNetwork(network).WithNetworkAliases("frps")
                .WithPortBinding(31001, true).WithPortBinding(31002, true).WithPortBinding(31003, true).WithPortBinding(31004, true).WithPortBinding(31005, true)
                .WithResourceMapping(binaries["frps"], "/frps", 65532, 65532, UnixFileModes.UserRead | UnixFileModes.UserExecute)
                .WithResourceMapping(tls.Ca, "/ca.crt").WithResourceMapping(tls.ServerCert, "/server.crt").WithResourceMapping(tls.ServerKey, "/server.key")
                .WithResourceMapping(Bytes($$"""
                    bindPort = 60999
                    allowPorts = [{ start = 31001, end = 31005 }]
                    auth.token = "{{token}}"
                    transport.tls.force = true
                    transport.tls.certFile = "/server.crt"
                    transport.tls.keyFile = "/server.key"
                    transport.tls.trustedCaFile = "/ca.crt"
                    """), "/frps.toml")
                .WithEntrypoint("/frps", "-c", "/frps.toml")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(60999));
            var sshImage = Environment.GetEnvironmentVariable("NOCTF_SSH_IMAGE") ?? "noctf-gateway-ssh-prototype:local";
            var sshServer = new ContainerBuilder(sshImage).WithNetwork(network).WithNetworkAliases("tunnel")
                .WithPortBinding(31001, true).WithPortBinding(31002, true).WithPortBinding(31003, true).WithPortBinding(31004, true).WithPortBinding(31005, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(60999));
            if (sshKeys is not null)
                sshServer = sshServer.WithResourceMapping(sshKeys.HostPrivate, "/config/host_key", 0, 0, UnixFileModes.UserRead)
                    .WithResourceMapping(Bytes("restrict,port-forwarding " + sshKeys.ClientPublic), "/config/authorized_keys")
                    .WithResourceMapping(Bytes(SshServerConfiguration), "/config/sshd_config")
                    .WithResourceMapping(Bytes(string.Join('\n', SshServerConfiguration.Split('\n').Select(line => line.StartsWith("PermitListen ", StringComparison.Ordinal)
                        ? "PermitListen " + string.Join(' ', Enumerable.Range(40000, 256).Select(port => "0.0.0.0:" + port))
                        : line))), "/config/range_config");
            await using var server = (ssh ? sshServer : frpServer)
                .WithCreateParameterModifier(parameters => { parameters.HostConfig!.Memory = 64 * 1024 * 1024; parameters.HostConfig.NanoCPUs = 250_000_000; }).Build();
            await server.StartAsync(ct);
            var frpClient = new ContainerBuilder(PublicGatewaySafetyPrototypeTests.Image)
                .WithNetwork(network)
                .WithVolumeMount(volume, "/endpoints", AccessMode.ReadOnly)
                .WithResourceMapping(binaries["frpc"], "/frpc", 65532, 65532, UnixFileModes.UserRead | UnixFileModes.UserExecute)
                .WithResourceMapping(tls.Ca, "/ca.crt").WithResourceMapping(tls.ClientCert, "/client.crt").WithResourceMapping(tls.ClientKey, "/client.key")
                .WithResourceMapping(Bytes($$"""
                    serverAddr = "frps"
                    serverPort = 60999
                    loginFailExit = false
                    auth.token = "{{token}}"
                    transport.tls.enable = true
                    transport.tls.serverName = "frps"
                    transport.tls.trustedCaFile = "/ca.crt"
                    transport.tls.certFile = "/client.crt"
                    transport.tls.keyFile = "/client.key"
                    [[proxies]]
                    name = "A"
                    type = "tcp"
                    remotePort = 31001
                    [proxies.plugin]
                    type = "unix_domain_socket"
                    unixPath = "/endpoints/{{a}}/listen/8080.sock"
                    [[proxies]]
                    name = "B"
                    type = "tcp"
                    remotePort = 31002
                    [proxies.plugin]
                    type = "unix_domain_socket"
                    unixPath = "/endpoints/{{b}}/listen/8080.sock"
                    [[proxies]]
                    name = "raw-A"
                    type = "tcp"
                    remotePort = 31003
                    [proxies.plugin]
                    type = "unix_domain_socket"
                    unixPath = "/endpoints/{{a}}/listen/8081.sock"
                    [[proxies]]
                    name = "framed-A"
                    type = "tcp"
                    remotePort = 31004
                    [proxies.plugin]
                    type = "unix_domain_socket"
                    unixPath = "/endpoints/{{a}}/listen/8082.sock"
                    [[proxies]]
                    name = "tls-A"
                    type = "tcp"
                    remotePort = 31005
                    [proxies.plugin]
                    type = "unix_domain_socket"
                    unixPath = "/endpoints/{{a}}/listen/8083.sock"
                    """), "/frpc.toml")
                .WithEntrypoint("/frpc", "-c", "/frpc.toml")
                .WithCreateParameterModifier(parameters => { parameters.User = "65532:65532"; parameters.HostConfig!.Memory = 64 * 1024 * 1024; parameters.HostConfig.NanoCPUs = 250_000_000; });
            var sshClient = new ContainerBuilder(sshImage).WithNetwork(network)
                .WithVolumeMount(volume, "/endpoints", AccessMode.ReadOnly)
                .WithEnvironment("AUTOSSH_GATETIME", "0").WithEnvironment("AUTOSSH_POLL", "3")
                .WithEntrypoint("autossh", "-M", "0", "-N", "-F", "/config/client.conf", "gateway")
                .WithCreateParameterModifier(parameters => { parameters.User = "65532:65532"; parameters.HostConfig!.Memory = 64 * 1024 * 1024; parameters.HostConfig.NanoCPUs = 250_000_000; });
            if (sshKeys is not null)
                sshClient = sshClient.WithResourceMapping(sshKeys.ClientPrivate, "/config/client_key", 65532, 65532, UnixFileModes.UserRead)
                    .WithResourceMapping(Bytes("[tunnel]:60999 " + sshKeys.HostPublic), "/config/known_hosts")
                    .WithResourceMapping(Bytes("[tunnel]:60999 " + sshKeys.ClientPublic), "/config/wrong_known_hosts")
                    .WithResourceMapping(Bytes(SshClientConfiguration), "/config/client.conf");
            await using var client = (ssh ? sshClient : frpClient).Build();
            await client.StartAsync(ct);
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
            var publicA = new Uri($"http://{server.Hostname}:{server.GetMappedPublicPort(31001)}/");
            var publicB = new Uri($"http://{server.Hostname}:{server.GetMappedPublicPort(31002)}/");
            if (ssh)
            {
                // Start with an empty master; only a control-plane decision adds publications.
                await Assert.That(await ReadAsync(http, publicA, ct)).IsNull();
                await Assert.That(await ReadAsync(http, publicB, ct)).IsNull();
                await AddSshForwardAsync(client, a, 31001, 8080, ct);
                await AddSshForwardAsync(client, b, 31002, 8080, ct);
                await AddSshForwardAsync(client, a, 31003, 8081, ct);
                await AddSshForwardAsync(client, a, 31004, 8082, ct);
                await AddSshForwardAsync(client, a, 31005, 8083, ct);
            }
            await ExpectAsync(http, publicA, "A", ct);
            await ExpectAsync(http, publicB, "B", ct);
            await CheckWebProtocolsAsync(http, server, publicA, targetTls.ServerCert, ct);
            // No HTTP parser in the data plane: duplicate headers, chunk syntax and arbitrary bytes remain intact.
            var prefix = Bytes("POST /a%2fb HTTP/1.1\r\nHost: original.test\r\nX-Test: a\r\nx-test: b\r\nTransfer-Encoding: chunked\r\n\r\n");
            var payload = prefix.Concat(RandomNumberGenerator.GetBytes(1024 * 1024)).ToArray();
            await Assert.That(await CheckHalfCloseAsync(targetA.Hostname, targetA.GetMappedPublicPort(8081), payload, "direct", ct)).IsTrue();
            var local = await leases.ExecAsync(["python", "-c", $$"""
                import socket, hashlib
                s=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM)
                s.settimeout(5)
                s.connect('/endpoints/{{a}}/listen/8081.sock')
                body=bytes(range(256))*4096
                s.sendall(body); s.shutdown(socket.SHUT_WR)
                result=bytearray()
                while data:=s.recv(16384): result.extend(data)
                assert result==body, (len(body),len(result))
                print('local Unix relay half-close PASS')
                """], ct);
            await Assert.That(local.ExitCode).IsEqualTo(0L);
            Console.WriteLine(local.Stdout);
            var sharedHalfClosePassed = await CheckHalfCloseAsync(server.Hostname, server.GetMappedPublicPort(31003), payload, ssh ? "shared SSH" : "shared FRP", ct);
            using (var framed = new TcpClient())
            {
                await framed.ConnectAsync(server.Hostname, server.GetMappedPublicPort(31004), ct);
                var stream = framed.GetStream();
                var length = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
                await stream.WriteAsync(length, ct);
                await stream.WriteAsync(payload, ct);
                using var response = new MemoryStream();
                await stream.CopyToAsync(response, ct);
                await Assert.That(response.ToArray().SequenceEqual(payload)).IsTrue();
            }
            var process = await relayA.ExecAsync(["/relay", "health", "/endpoint", "/lease"], ct);
            await Assert.That(process.ExitCode).IsEqualTo(0L);
            var tamper = await relayA.ExecAsync(["touch", "/lease/unauthorized-renewal"], ct);
            await Assert.That(tamper.ExitCode).IsNotEqualTo(0L);
            Console.WriteLine($"SHARED {(ssh ? "SSH" : "FRPC")}: two runtimes, five ports; 1 MiB framed opaque bytes PASS; half-close={sharedHalfClosePassed}; relay caps 16 MiB / 0.05 CPU.");
            if (ssh)
            {
                // Parse a bounded public pool, not a huge per-port list for the entire ephemeral range.
                var range = await server.ExecAsync(["/usr/sbin/sshd", "-t", "-f", "/config/range_config"], ct);
                await Assert.That(range.ExitCode).IsEqualTo(0L);
                var forbiddenPort = await client.ExecAsync(["ssh", "-F", "none", "-S", "/control/master", "-O", "forward", "-R", $"0.0.0.0:31006:/endpoints/{b}/listen/8080.sock", "gateway"], ct);
                await Assert.That(forbiddenPort.ExitCode).IsNotEqualTo(0L);
                var forbiddenCommand = await client.ExecAsync(["ssh", "-F", "/config/client.conf", "gateway", "id"], ct);
                await Assert.That(forbiddenCommand.ExitCode).IsNotEqualTo(0L);
                var forbiddenSubsystem = await client.ExecAsync(["ssh", "-F", "/config/client.conf", "-s", "gateway", "sftp"], ct);
                await Assert.That(forbiddenSubsystem.ExitCode).IsNotEqualTo(0L);
                var forbiddenLocalForward = await client.ExecAsync(["ssh", "-F", "/config/client.conf", "-W", "127.0.0.1:60999", "gateway"], ct);
                await Assert.That(forbiddenLocalForward.ExitCode).IsNotEqualTo(0L);
                var badIdentity = await client.ExecAsync(["ssh", "-F", "/config/client.conf", "-S", "none", "-o", "ControlMaster=no", "-o", "ClearAllForwardings=yes", "-o", "UserKnownHostsFile=/config/wrong_known_hosts", "-N", "gateway"], ct);
                await Assert.That(badIdentity.ExitCode).IsNotEqualTo(0L);
                var canceled = await client.ExecAsync(["ssh", "-F", "none", "-S", "/control/master", "-O", "cancel", "-R", $"0.0.0.0:31002:/endpoints/{b}/listen/8080.sock", "gateway"], ct);
                await Assert.That(canceled.ExitCode).IsEqualTo(0L);
                await Assert.That(await ReadAsync(http, publicB, ct)).IsNull();
                await ExpectAsync(http, publicA, "A", ct);
                var added = await client.ExecAsync(["ssh", "-F", "none", "-S", "/control/master", "-O", "forward", "-R", $"0.0.0.0:31002:/endpoints/{b}/listen/8080.sock", "gateway"], ct);
                await Assert.That(added.ExitCode).IsEqualTo(0L);
                await ExpectAsync(http, publicB, "B", ct);
                Console.WriteLine("SSH: wrong host identity, unapproved port, command, SFTP and local forwarding rejected; live cancel/add B preserved A.");
            }
            foreach (var observed in new[] { relayA, relayB, client, server })
            {
                var status = await observed.ExecAsync(["cat", "/proc/1/status"], ct);
                Console.WriteLine($"{observed.Id[..12]} " + string.Join("; ", status.Stdout.Split('\n').Where(line => line.StartsWith("VmRSS:", StringComparison.Ordinal) || line.StartsWith("Threads:", StringComparison.Ordinal))));
                var cgroup = await observed.ExecAsync(["cat", "/sys/fs/cgroup/memory.current"], ct);
                Console.WriteLine($"{observed.Id[..12]} cgroup memory bytes: {cgroup.Stdout.Trim()}");
            }
            // Management-external deletion: old A must never dial a replacement reusing its host port.
            await targetA.DisposeAsync();
            await using var replacement = Target("C").WithPortBinding(aPort, 8080).Build();
            await replacement.StartAsync(ct);
            await ExpectAsync(http, new Uri($"http://{replacement.Hostname}:{aPort}/"), "C", ct);
            await Assert.That(await ReadAsync(http, publicA, ct)).IsNull();
            await ExpectAsync(http, publicB, "B", ct);
            await client.StopAsync(ct); await client.StartAsync(ct);
            if (ssh)
            {
                await Assert.That(await ReadAsync(http, publicB, ct)).IsNull();
                await AddSshForwardAsync(client, b, 31002, 8080, ct);
            }
            await ExpectAsync(http, publicB, "B", ct);
            await Assert.That(await ReadAsync(http, publicA, ct)).IsNull();
            await server.StopAsync(ct); await server.StartAsync(ct);
            // Docker may allocate different random host ports when the FRPS fixture restarts.
            var restarted = await docker.Containers.InspectContainerAsync(server.Id, ct);
            publicA = new Uri($"http://{server.Hostname}:{restarted.NetworkSettings!.Ports!["31001/tcp"][0].HostPort}/");
            publicB = new Uri($"http://{server.Hostname}:{restarted.NetworkSettings!.Ports!["31002/tcp"][0].HostPort}/");
            if (ssh)
            {
                await Assert.That(await ReadAsync(http, publicB, ct)).IsNull();
                await AddSshForwardAsync(client, b, 31002, 8080, ct);
            }
            await ExpectAsync(http, publicB, "B", ct);
            await Assert.That(await ReadAsync(http, publicA, ct)).IsNull();
            await relayA.StopAsync(ct);
            await ExpectAsync(http, publicB, "B", ct);
            // Missing renewal revokes only B. Its ordinary Docker endpoint remains healthy.
            await leases.ExecAsync(["touch", $"/endpoints/{b}/stop-renew"], ct);
            await Task.Delay(TimeSpan.FromSeconds(11), ct);
            await Assert.That(await ReadAsync(http, publicB, ct)).IsNull();
            await ExpectAsync(http, new Uri($"http://{targetB.Hostname}:{targetB.GetMappedPublicPort(8080)}/"), "B", ct);
            await leases.ExecAsync(["rm", $"/endpoints/{b}/stop-renew"], ct);
            await Task.Delay(750, ct);
            await docker.Containers.StartContainerAsync(relayB.Id, new ContainerStartParameters(), ct);
            await Task.Delay(500, ct);
            await Assert.That((await docker.Containers.InspectContainerAsync(relayB.Id, ct)).State?.Running ?? false).IsFalse();
            Console.WriteLine($"SHARED {(ssh ? "SSH" : "FRPC")}: unmanaged port reuse, tunnel restart, delayed A cleanup, lease expiry and stale relay restart PASS.");
            // FRP remains an explicit negative control. Only the SSH candidate may pass transparency.
            await Assert.That(sharedHalfClosePassed).IsEqualTo(ssh);
        });
    }

    private static ContainerBuilder Target(string identity) => new ContainerBuilder(PublicGatewaySafetyPrototypeTests.Image)
        .WithEnvironment("IDENTITY", identity).WithResourceMapping(Bytes(TargetServer), "/server.py")
        .WithEntrypoint("python", "/server.py")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080));
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private const string SshServerConfiguration = """
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
        AuthenticationMethods publickey
        AllowTcpForwarding remote
        AllowStreamLocalForwarding no
        GatewayPorts clientspecified
        PermitListen 0.0.0.0:31001 0.0.0.0:31002 0.0.0.0:31003 0.0.0.0:31004 0.0.0.0:31005
        PermitOpen none
        MaxSessions 0
        PermitTTY no
        PermitTunnel no
        AllowAgentForwarding no
        X11Forwarding no
        PermitUserRC no
        ForceCommand /bin/false
        LoginGraceTime 10
        MaxAuthTries 2
        MaxStartups 3:30:6
        ClientAliveInterval 3
        ClientAliveCountMax 3
        Compression no
        LogLevel ERROR
        """;
    private static async Task AddSshForwardAsync(IContainer client, string publication, int publicPort, int containerPort, CancellationToken ct)
    {
        // Local disposable fixture only: retry the same OpenSSH master forwarding command
        // while autossh reconnects. No business facts or external platform side effects.
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var result = await client.ExecAsync(["ssh", "-F", "none", "-S", "/control/master", "-O", "forward", "-R",
                $"0.0.0.0:{publicPort}:/endpoints/{publication}/listen/{containerPort}.sock", "gateway"], ct);
            if (result.ExitCode == 0) return;
            await Task.Delay(250, ct);
        }
        throw new InvalidOperationException("Shared SSH master could not bind the isolated fixture publication.");
    }

    private const string SshClientConfiguration = """
        Host gateway
          HostName tunnel
          Port 60999
          User noctf
          IdentityFile /config/client_key
          UserKnownHostsFile /config/known_hosts
          GlobalKnownHostsFile /dev/null
          StrictHostKeyChecking yes
          IdentitiesOnly yes
          IdentityAgent none
          BatchMode yes
          RequestTTY no
          ExitOnForwardFailure yes
          ServerAliveInterval 3
          ServerAliveCountMax 3
          ConnectTimeout 5
          ConnectionAttempts 1
          Compression no
          ControlMaster yes
          ControlPath /control/master
          ControlPersist no
        """;

    private static async Task CheckWebProtocolsAsync(HttpClient http, IContainer server, Uri publicA, byte[] targetCertificate, CancellationToken ct)
    {
        using var expectedCertificate = X509Certificate2.CreateFromPem(Encoding.UTF8.GetString(targetCertificate));
        var pin = expectedCertificate.GetCertHash(HashAlgorithmName.SHA256);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(server.Hostname, server.GetMappedPublicPort(31005), ct);
                using var tls = new SslStream(tcp.GetStream(), false, (_, certificate, _, errors) => certificate is not null
                    && !errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)
                    && CryptographicOperations.FixedTimeEquals(pin, certificate.GetCertHash(HashAlgorithmName.SHA256)));
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "frps" }, ct);
                await tls.WriteAsync(Bytes("GET / HTTP/1.1\r\nHost: original-target.test\r\nConnection: close\r\n\r\n"), ct);
                using var text = new StreamReader(tls, leaveOpen: true);
                await Assert.That(await text.ReadToEndAsync(ct)).Contains("\r\n\r\nA");
                break;
            }
            catch (IOException) when (attempt < 9) { await Task.Delay(250, ct); }
            catch (SocketException) when (attempt < 9) { await Task.Delay(250, ct); }
        }
        using (var response = await http.GetAsync(new Uri(publicA, "/events"), HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            using var events = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
            // The server delays its second event by five seconds. First-event delivery must not wait for EOF.
            await Assert.That(await events.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(3), ct)).IsEqualTo("data: first");
        }
        using var socket = new ClientWebSocket();
        socket.Options.Proxy = null;
        await socket.ConnectAsync(new UriBuilder(publicA) { Scheme = "ws", Path = "/ws" }.Uri, ct);
        var message = RandomNumberGenerator.GetBytes(48);
        await socket.SendAsync(message.AsMemory(), WebSocketMessageType.Binary, true, ct);
        var received = new byte[256];
        var result = await socket.ReceiveAsync(received.AsMemory(), ct);
        await Assert.That(result.MessageType).IsEqualTo(WebSocketMessageType.Binary);
        await Assert.That(received.AsSpan(0, result.Count).SequenceEqual(message)).IsTrue();
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
        Console.WriteLine("WEB: target-owned TLS certificate, early SSE delivery and binary WebSocket roundtrip PASS.");
    }

    private sealed class EphemeralSshKeys : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("noctf-ssh-prototype-");
        public byte[] HostPrivate { get; private set; } = [];
        public byte[] ClientPrivate { get; private set; } = [];
        public string HostPublic { get; private set; } = "";
        public string ClientPublic { get; private set; } = "";
        public static async Task<EphemeralSshKeys> CreateAsync(CancellationToken ct)
        {
            var keys = new EphemeralSshKeys();
            try
            {
                foreach (var name in new[] { "host", "client" })
                {
                    var info = new System.Diagnostics.ProcessStartInfo("ssh-keygen") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    foreach (var arg in new[] { "-q", "-t", "ed25519", "-N", "", "-C", "isolated-gateway-test", "-f", Path.Combine(keys.directory.FullName, name) }) info.ArgumentList.Add(arg);
                    using var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("Cannot start ssh-keygen.");
                    try { await process.WaitForExitAsync(ct); }
                    catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
                    if (process.ExitCode != 0) throw new InvalidOperationException("Ephemeral SSH key generation failed.");
                }
                keys.HostPrivate = await File.ReadAllBytesAsync(Path.Combine(keys.directory.FullName, "host"), ct);
                keys.ClientPrivate = await File.ReadAllBytesAsync(Path.Combine(keys.directory.FullName, "client"), ct);
                keys.HostPublic = await File.ReadAllTextAsync(Path.Combine(keys.directory.FullName, "host.pub"), ct);
                keys.ClientPublic = await File.ReadAllTextAsync(Path.Combine(keys.directory.FullName, "client.pub"), ct);
                return keys;
            }
            catch { keys.Dispose(); throw; }
        }
        public void Dispose() => directory.Delete(recursive: true);
    }
    private static async Task<bool> CheckHalfCloseAsync(string host, int port, byte[] payload, string label, CancellationToken ct)
    {
        using var connection = new TcpClient();
        await connection.ConnectAsync(host, port, ct);
        var stream = connection.GetStream();
        await stream.WriteAsync(payload, ct);
        connection.Client.Shutdown(SocketShutdown.Send);
        using var received = new MemoryStream();
        await stream.CopyToAsync(received, ct);
        Console.WriteLine($"{label} half-close: sent={payload.Length}, received={received.Length}");
        return received.ToArray().SequenceEqual(payload);
    }
    private static async Task<string?> ReadAsync(HttpClient http, Uri address, CancellationToken ct)
    {
        try { return await http.GetStringAsync(address, ct); }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }
    private static async Task ExpectAsync(HttpClient http, Uri address, string expected, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (await ReadAsync(http, address, ct) == expected) return;
            await Task.Delay(200, ct);
        }
        throw new InvalidOperationException("Shared tunnel endpoint did not reach expected synthetic target.");
    }
    private const string LeaseWriter = """
        import json, os, pathlib, sys, time
        roots = [(pathlib.Path('/endpoints') / identity, identity) for identity in sys.argv[1:]]
        for root, identity in roots:
            root.mkdir(mode=0o700)
            os.chown(root, 65532, 65532)
            (root/'listen').mkdir(mode=0o700)
            os.chown(root/'listen',65532,65532)
            (root/'control').mkdir(mode=0o750)
            os.chown(root/'control',0,65532)
        pathlib.Path('/initialized').touch()
        while True:
            for root, identity in roots:
                if (root / 'stop-renew').exists(): continue
                path = root / 'control' / 'lease.next'
                path.write_text(json.dumps({'publicationId': identity, 'expiresAtUnixMs': int((time.time()+9)*1000)}))
                os.chown(path, 0, 65532)
                os.chmod(path, 0o640)
                os.replace(path, root / 'control' / 'lease.json')
            time.sleep(0.5)
        """;
    private const string TargetServer = """
        import asyncio, os, pathlib, ssl, base64, hashlib
        async def http(reader, writer):
            try:
                head=await reader.readuntil(b'\r\n\r\n')
                path=head.split(b' ')[1]
                if path==b'/events':
                    writer.write(b'HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\ndata: first\n\n')
                    await writer.drain(); await asyncio.sleep(5)
                    writer.write(b'data: second\n\n'); await writer.drain(); return
                if path==b'/ws':
                    key=next(line.split(b':',1)[1].strip() for line in head.split(b'\r\n') if line.lower().startswith(b'sec-websocket-key:'))
                    accept=base64.b64encode(hashlib.sha1(key+b'258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest())
                    writer.write(b'HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: '+accept+b'\r\n\r\n')
                    await writer.drain()
                    while True:
                        flags,size=await reader.readexactly(2)
                        assert size&128
                        size&=127
                        if size==126: size=int.from_bytes(await reader.readexactly(2),'big')
                        if size==127: size=int.from_bytes(await reader.readexactly(8),'big')
                        assert size<1024*1024
                        mask=await reader.readexactly(4)
                        payload=await reader.readexactly(size)
                        payload=bytes(value^mask[i%4] for i,value in enumerate(payload))
                        assert size<126
                        writer.write(bytes([flags,size])+payload); await writer.drain()
                        if flags&15==8: return
                body = os.environ['IDENTITY'].encode()
                writer.write(b'HTTP/1.1 200 OK\r\nContent-Length: '+str(len(body)).encode()+b'\r\nConnection: close\r\n\r\n'+body)
                await writer.drain()
            finally: writer.close()
        async def raw(reader, writer):
            data = bytearray()
            while chunk := await reader.read(16384):
                data.extend(chunk)
                if len(data) > 2*1024*1024:
                    writer.close(); return
            writer.write(data)
            await writer.drain()
            writer.close()
        async def main():
            a = await asyncio.start_server(http, '0.0.0.0', 8080)
            b = await asyncio.start_server(raw, '0.0.0.0', 8081)
            c = await asyncio.start_server(framed, '0.0.0.0', 8082)
            if pathlib.Path('/target.crt').exists():
                context=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
                context.load_cert_chain('/target.crt','/target.key')
                await asyncio.start_server(http,'0.0.0.0',8083,ssl=context)
            async with a, b, c: await asyncio.Future()
        async def framed(reader, writer):
            size=int.from_bytes(await reader.readexactly(4),'big')
            if size>2*1024*1024: writer.close(); return
            writer.write(await reader.readexactly(size))
            await writer.drain()
            writer.close()
        asyncio.run(main())
        """;
}

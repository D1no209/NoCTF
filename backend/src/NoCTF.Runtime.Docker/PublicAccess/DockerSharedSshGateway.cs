using System.Collections.Concurrent;
using System.Formats.Tar;
using System.Globalization;
using System.Net;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Domain.Platform;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Runtime.Docker.PublicAccess;

/// <summary>Deployment-only SSH credentials and bind paths; these values never come from a browser.</summary>
public sealed record SharedSshGatewayOptions(string ConnectorId, string RunnerId, string ClientImage, string RelayImage,
    string ServerHost, int ServerPort, string PrivateKeyFile, string KnownHostsFile,
    string LocalStateDirectory, string HostStateDirectory, IReadOnlyList<int> PublicPorts);

/// <summary>One shared SSH client per ownership session, one byte-transparent endpoint per Runtime.</summary>
public sealed class DockerSharedSshGateway : IPublicGatewayTransport, IDisposable
{
    private const string ConnectorLabel = "noctf.io/public-gateway-connector";
    private const string RunnerLabel = "noctf.io/public-gateway-runner";
    private const string SessionLabel = "noctf.io/gateway-session";
    private const string RoleLabel = "noctf.io/gateway-role";
    private readonly DockerClient docker;
    private readonly SharedSshGatewayOptions options;
    private readonly Lazy<GatewayLeaseDirectory> leaseDirectories;
    private GatewayLeaseDirectory directories => leaseDirectories.Value;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim clientGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, Publication> publications = new();
    private string? clientId;
    private SharedSshPortController? controller;
    public bool UsesIndependentPublicPorts => true;
    private volatile bool requiresReset;
    public bool RequiresReset => requiresReset;

    public DockerSharedSshGateway(DockerRuntimeOptions dockerOptions, SharedSshGatewayOptions options, TimeProvider clock)
    {
        this.options = options;
        this.clock = clock;
        leaseDirectories = new(() => new(options.LocalStateDirectory, options.HostStateDirectory));
        docker = new DockerClientBuilder().WithEndpoint(new Uri(dockerOptions.Endpoint)).Build();
    }

    public async Task<GatewayPublication> StartAsync(Guid runtimeId, string targetId, IReadOnlyList<RuntimePublishedPortView> ports, CancellationToken ct)
    {
        if (!await VerifyTargetAsync(runtimeId, targetId, ports, ct)) throw new PublicTunnelException(PublicAccessFailure.GatewayIdentityRejected);
        await EnsureClientAsync(ct);
        var id = Guid.NewGuid();
        await directories.CreateAsync(id, ct);
        var labels = Labels("relay");
        labels["noctf.io/publication-id"] = id.ToString("D");
        labels["noctf.io/publication-runtime"] = runtimeId.ToString("D");
        labels["noctf.io/publication-target"] = targetId;
        string? helperId = null;
        try
        {
            var created = await docker.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Name = $"noctf-relay-{id:N}", Image = options.RelayImage, User = directories.User, Labels = labels,
                Entrypoint = ["/relay"], Cmd = ["run", "/endpoint", "/lease", id.ToString("D"), "32", .. ports.Select(port => port.ContainerPort.ToString(CultureInfo.InvariantCulture))],
                HostConfig = new HostConfig
                {
                    NetworkMode = "container:" + targetId,
                    Binds = [$"{directories.HostPublication(id)}/listen:/endpoint", $"{directories.HostPublication(id)}/control:/lease:ro"],
                    Memory = 16 * 1024 * 1024, NanoCPUs = 50_000_000, PidsLimit = 32,
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No }, LogConfig = Logs()
                }
            }, ct);
            helperId = created.ID;
            var publication = new GatewayPublication(id, runtimeId, targetId, helperId, ports);
            publications[id] = new(publication);
            // Created but not running. Guarded RenewAsync writes the first lease; ReadStatusAsync starts
            // the process and publishes outside the database transaction.
            return publication;
        }
        catch
        {
            if (helperId is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await docker.Containers.RemoveContainerAsync(helperId, new ContainerRemoveParameters { Force = true }, cleanup.Token);
            }
            directories.Remove(directories.SessionId, id);
            throw;
        }
    }

    public async Task RenewAsync(GatewayPublication publication, DateTimeOffset expiresAt, CancellationToken ct)
    {
        if (!publications.TryGetValue(publication.Id, out var state) || state.Value != publication)
            throw new PublicTunnelException(PublicAccessFailure.GatewayIdentityRejected);
        await state.LeaseGate.WaitAsync(ct);
        try
        {
            if (state.Revoked) throw new PublicTunnelException(PublicAccessFailure.GatewayIdentityRejected);
            if (expiresAt <= clock.GetUtcNow() || expiresAt > clock.GetUtcNow().AddSeconds(10))
                throw new ArgumentOutOfRangeException(nameof(expiresAt));
            await directories.RenewAsync(publication.Id, expiresAt, ct);
            state.ExpiresAt = expiresAt;
        }
        finally { state.LeaseGate.Release(); }
    }

    public async Task<IReadOnlyList<PublicEndpointStatus>> ReadStatusAsync(GatewayPublication publication, CancellationToken ct)
    {
        var activeController = controller;
        if (!publications.TryGetValue(publication.Id, out var state) || state.Value != publication || state.Revoked
            || state.ExpiresAt <= clock.GetUtcNow() || activeController is null
            || !await VerifyTargetAsync(publication.RuntimeId, publication.TargetId, publication.Ports, ct))
            throw new PublicTunnelException(PublicAccessFailure.GatewayIdentityRejected);
        var helper = await docker.Containers.InspectContainerAsync(publication.HelperId, ct);
        if (!OwnsRelay(helper, publication)) throw new PublicTunnelException(PublicAccessFailure.GatewayIdentityRejected);
        if (helper.State?.Status == "created")
            await docker.Containers.StartContainerAsync(publication.HelperId, new ContainerStartParameters(), ct);
        else if (helper.State?.Running != true)
            throw new PublicTunnelException(PublicAccessFailure.GatewayIdentityRejected);
        // Ready marker is local filesystem state, not an exec/probe process for every Runtime.
        var readyFile = Path.Combine(directories.LocalPublication(publication.Id), "listen", "ready");
        if (!File.Exists(readyFile)) return publication.Ports.Select(port => new PublicEndpointStatus(port.ContainerPort, port.HostPort,
            PublicAccessState.Pending, PublicAccessFailure.GatewayReconciliationPending)).ToArray();
        var info = new FileInfo(readyFile);
        // Only existence is needed: this is a fresh private directory written by the fixed relay binary.
        // Do not open an untrusted special file here (a FIFO could block a status check).
        if (info.Length > 64 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new PublicTunnelException(PublicAccessFailure.GatewayIdentityRejected);
        var endpoints = new List<PublicEndpointStatus>();
        foreach (var port in publication.Ports)
        {
            var lease = await activeController.PublishAsync(publication.Id, port.ContainerPort, ct);
            endpoints.Add(new(port.ContainerPort, port.HostPort, PublicAccessState.Ready, null, lease.PublicPort));
        }
        if (!ReferenceEquals(activeController, controller) || state.Revoked || state.ExpiresAt <= clock.GetUtcNow())
            throw new PublicTunnelException(PublicAccessFailure.GatewayReconciliationPending);
        return endpoints;
    }

    public async Task RevokeAsync(GatewayPublication publication, CancellationToken ct)
    {
        if (publications.TryGetValue(publication.Id, out var state))
        {
            await state.LeaseGate.WaitAsync(ct);
            try { state.Revoked = true; directories.Invalidate(publication.Id); }
            finally { state.LeaseGate.Release(); }
        }
        try
        {
            var helper = await docker.Containers.InspectContainerAsync(publication.HelperId, ct);
            if (!OwnsRelay(helper, publication)) throw new PublicTunnelException(PublicAccessFailure.GatewayIdentityRejected);
            await docker.Containers.RemoveContainerAsync(publication.HelperId, new ContainerRemoveParameters { Force = true }, ct);
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { }
        // Data plane is gone before waiting for remote acknowledgment. Failed cancels keep the reservation.
        if (controller is not null)
        {
            var masterExists = false;
            if (clientId is not null)
            {
                try { masterExists = (await docker.Containers.InspectContainerAsync(clientId, ct)).State?.Running == true; }
                catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { }
            }
            if (masterExists) await controller.RevokePublicationAsync(publication.Id, ct);
            else requiresReset = true; // The immutable old client is gone, not merely disconnected.
        }
        directories.Remove(directories.SessionId, publication.Id);
        publications.TryRemove(publication.Id, out _);
    }

    public async Task RevokeStaleHelpersAsync(CancellationToken ct)
    {
        // Only the connector owner calls this. A new owner creates an entirely new client container ID;
        // old in-flight execs cannot reach the replacement master by a reusable container name.
        var resources = await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true, Filters = new Dictionary<string, IDictionary<string, bool>>
            { ["label"] = new Dictionary<string, bool> { [$"{ConnectorLabel}={options.ConnectorId}"] = true, [$"{RunnerLabel}={options.RunnerId}"] = true } }
        }, ct);
        if (resources.Count > 512) throw new PublicTunnelException(PublicAccessFailure.GatewayCapacityExceeded);
        // Client first: revoke the whole old data path before cleaning individual endpoints.
        foreach (var resource in resources.OrderBy(item => Label(item.Labels, RoleLabel) == "client" ? 0 : 1))
        {
            var inspected = await docker.Containers.InspectContainerAsync(resource.ID, ct);
            var role = Label(inspected.Config?.Labels, RoleLabel);
            if (Label(inspected.Config?.Labels, ConnectorLabel) != options.ConnectorId || Label(inspected.Config?.Labels, RunnerLabel) != options.RunnerId
                ) continue;
            if (!Guid.TryParse(Label(resource.Labels, SessionLabel), out var session))
            {
                // Migration from the former isolated FRP helper. These reserved labels and immutable
                // target binding are generated by NoCTF, never supplied by challenge definitions.
                if (role is not null || !Guid.TryParse(Label(resource.Labels, "noctf.io/publication-id"), out _)
                    || !Guid.TryParse(Label(resource.Labels, "noctf.io/publication-runtime"), out _)
                    || Label(resource.Labels, "noctf.io/publication-target") is not { Length: 64 } legacyTarget
                    || inspected.HostConfig?.NetworkMode != "container:" + legacyTarget) continue;
                await docker.Containers.RemoveContainerAsync(resource.ID, new ContainerRemoveParameters { Force = true }, ct);
                continue;
            }
            if (role is not ("client" or "relay")) continue;
            if (role == "relay" && (Label(inspected.Config?.Labels, "noctf.io/publication-target") is not { Length: 64 } target
                || inspected.HostConfig?.NetworkMode != "container:" + target)) continue;
            await docker.Containers.RemoveContainerAsync(resource.ID, new ContainerRemoveParameters { Force = true }, ct);
            if (role == "relay" && Guid.TryParse(Label(resource.Labels, "noctf.io/publication-id"), out var publication))
                directories.Remove(session, publication);
        }
        publications.Clear();
        controller?.Dispose(); controller = null; clientId = null;
        requiresReset = false;
    }

    private async Task EnsureClientAsync(CancellationToken ct)
    {
        await clientGate.WaitAsync(ct);
        try
        {
            if (clientId is not null)
            {
                try
                {
                    var client = await docker.Containers.InspectContainerAsync(clientId, ct);
                    if (client.State?.Running != true) await docker.Containers.StartContainerAsync(clientId, new ContainerStartParameters(), ct);
                    return;
                }
                catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
                { controller?.Dispose(); controller = null; clientId = null; }
            }
            var key = await ReadCredentialAsync(options.PrivateKeyFile, ct);
            var hosts = await ReadCredentialAsync(options.KnownHostsFile, ct);
            if (key.Length is < 64 or > 16384 || hosts.Length is < 32 or > 16384)
                throw new PublicTunnelException(PublicAccessFailure.GatewaySafetyCheckFailed);
            var created = await docker.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Name = $"noctf-ssh-{directories.SessionId:N}-{Guid.NewGuid():N}", Image = options.ClientImage,
                User = directories.User, Labels = Labels("client"), Env = ["AUTOSSH_GATETIME=0", "AUTOSSH_POLL=3"],
                HostConfig = new HostConfig
                {
                    Binds = [$"{directories.HostSession}:/endpoints:ro"], Memory = 64 * 1024 * 1024, NanoCPUs = 250_000_000,
                    // autossh does not reap the orphaned watchdog children of BusyBox timeout execs.
                    // Docker's tiny init must own PID 1, otherwise repeated control probes exhaust PIDs.
                    Init = true, PidsLimit = 64, RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }, LogConfig = Logs()
                }
            }, ct);
            try
            {
                var uid = int.Parse(directories.User.Split(':')[0], CultureInfo.InvariantCulture);
                var gid = int.Parse(directories.User.Split(':')[1], CultureInfo.InvariantCulture);
                using var archive = new MemoryStream();
                using (var writer = new TarWriter(archive, leaveOpen: true))
                {
                    foreach (var path in new[] { "config", "control" })
                        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.Directory, path) { Uid = uid, Gid = gid,
                            Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute }, ct);
                    // OpenSSH requires getpwuid() to resolve the local non-root UID. A non-root Runner
                    // uses its own UID for bind permissions, which may differ from the image's default.
                    foreach (var (name, content) in new Dictionary<string, string>
                    {
                        ["etc/passwd"] = FormattableString.Invariant($"root:x:0:0:root:/root:/bin/sh\nnoctf:x:{uid}:{gid}:gateway:/home/noctf:/sbin/nologin\n"),
                        ["etc/group"] = FormattableString.Invariant($"root:x:0:\nnoctf:x:{gid}:\n")
                    })
                    {
                        using var data = new MemoryStream(Encoding.UTF8.GetBytes(content));
                        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, name) { Uid = 0, Gid = 0,
                            Mode = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead, DataStream = data }, ct);
                    }
                    foreach (var (name, bytes) in new Dictionary<string, byte[]> { ["config/key"] = key, ["config/known_hosts"] = hosts,
                                 ["config/client.conf"] = Encoding.UTF8.GetBytes(ClientConfiguration()) })
                    {
                        using var data = new MemoryStream(bytes);
                        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, name)
                        { Uid = uid, Gid = gid, Mode = UnixFileMode.UserRead, DataStream = data }, ct);
                    }
                }
                archive.Position = 0;
                await docker.Containers.ExtractArchiveToContainerAsync(created.ID, new CopyToContainerParameters { Path = "/" }, archive, ct);
                await docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), ct);
                controller = new(new DockerSshControl(docker, created.ID), options.PublicPorts);
                clientId = created.ID;
            }
            catch
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await docker.Containers.RemoveContainerAsync(created.ID, new ContainerRemoveParameters { Force = true }, cleanup.Token);
                throw;
            }
        }
        finally { clientGate.Release(); }
    }

    private string ClientConfiguration() => FormattableString.Invariant($"""
        Host gateway
          HostName {options.ServerHost}
          Port {options.ServerPort}
          User noctf
          IdentityFile /config/key
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
        """);

    private static async Task<byte[]> ReadCredentialAsync(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 32 or > 16384 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new PublicTunnelException(PublicAccessFailure.GatewaySafetyCheckFailed);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var buffer = new byte[16385];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), ct);
            if (read == 0) return buffer[..count];
            count += read;
        }
        throw new PublicTunnelException(PublicAccessFailure.GatewaySafetyCheckFailed);
    }

    private async Task<bool> VerifyTargetAsync(Guid runtimeId, string targetId, IReadOnlyList<RuntimePublishedPortView> ports, CancellationToken ct)
    {
        if (targetId.Length != 64 || !targetId.All(char.IsAsciiHexDigit) || ports.Count is 0 or > 64) return false;
        ContainerInspectResponse target;
        try { target = await docker.Containers.InspectContainerAsync(targetId, ct); }
        catch (DockerApiException e) when (e.StatusCode == HttpStatusCode.NotFound) { return false; }
        return target.State?.Running == true && Label(target.Config?.Labels, "noctf.io/runtime-instance-id") == runtimeId.ToString()
            && ports.All(port => port.ServiceName is null && port.ContainerPort is > 0 and <= 65535
                && target.NetworkSettings?.Ports?.TryGetValue($"{port.ContainerPort}/tcp", out var bindings) == true
                && bindings is { Count: > 0 } && bindings.All(binding => binding.HostPort == port.HostPort.ToString(CultureInfo.InvariantCulture)));
    }
    private bool OwnsRelay(ContainerInspectResponse helper, GatewayPublication publication) => helper.Config?.Image == options.RelayImage
        && helper.HostConfig?.NetworkMode == "container:" + publication.TargetId
        && Label(helper.Config?.Labels, ConnectorLabel) == options.ConnectorId && Label(helper.Config?.Labels, RunnerLabel) == options.RunnerId
        && Label(helper.Config?.Labels, SessionLabel) == directories.SessionId.ToString("D") && Label(helper.Config?.Labels, RoleLabel) == "relay"
        && Label(helper.Config?.Labels, "noctf.io/publication-id") == publication.Id.ToString("D")
        && Label(helper.Config?.Labels, "noctf.io/publication-runtime") == publication.RuntimeId.ToString("D");
    private Dictionary<string, string> Labels(string role) => new() { [ConnectorLabel] = options.ConnectorId, [RunnerLabel] = options.RunnerId,
        [SessionLabel] = directories.SessionId.ToString("D"), [RoleLabel] = role };
    private static string? Label(IDictionary<string, string>? labels, string key) => labels is not null && labels.TryGetValue(key, out var value) ? value : null;
    private static LogConfig Logs() => new() { Type = "local", Config = new Dictionary<string, string> { ["max-size"] = "1m", ["max-file"] = "2" } };
    public async Task StopAsync(CancellationToken ct)
    {
        // Only this adapter's immutable client ID, never the next owner's client or the website tunnel.
        if (clientId is not null)
        {
            try { await docker.Containers.RemoveContainerAsync(clientId, new ContainerRemoveParameters { Force = true }, ct); }
            catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { }
        }
        foreach (var state in publications.Values)
        {
            await state.LeaseGate.WaitAsync(ct);
            try { state.Revoked = true; directories.Invalidate(state.Value.Id); }
            finally { state.LeaseGate.Release(); }
        }
    }
    public void Dispose() { controller?.Dispose(); clientGate.Dispose(); docker.Dispose(); }
    private sealed class Publication(GatewayPublication value)
    {
        public GatewayPublication Value { get; } = value;
        public SemaphoreSlim LeaseGate { get; } = new(1, 1);
        public volatile bool Revoked;
        private long expiry;
        public DateTimeOffset ExpiresAt
        {
            get => DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref expiry));
            set => Interlocked.Exchange(ref expiry, value.ToUnixTimeMilliseconds());
        }
    }
}

using System.Formats.Tar;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Domain.Platform;

namespace NoCTF.Runtime.Docker.PublicAccess;

public sealed record GatewayTransportOptions(string ConnectorId, string RunnerId, string HelperImage,
    string ServerHost, int ServerPort, string ServerName, string CaFile, string CertificateFile, string KeyFile, string TokenFile);
public sealed record GatewayPublication(Guid Id, Guid RuntimeId, string TargetId, string HelperId,
    IReadOnlyList<RuntimePublishedPortView> Ports, int AdminPort = 0);

/// <summary>Controls isolated FRP containers. No player bytes pass through this adapter.</summary>
public sealed class DockerPublicGateway : IPublicGatewayTransport, IDisposable
{
    public bool UsesIndependentPublicPorts => false;
    private const string ConnectorLabel = "noctf.io/public-gateway-connector";
    private readonly DockerClient docker;
    private readonly GatewayTransportOptions options;
    private readonly TimeProvider clock;
    public DockerPublicGateway(DockerRuntimeOptions dockerOptions, GatewayTransportOptions options, TimeProvider? clock = null)
    {
        this.options = options;
        this.clock = clock ?? TimeProvider.System;
        docker = new DockerClientBuilder().WithEndpoint(new Uri(dockerOptions.Endpoint)).Build();
    }

    public async Task<bool> VerifyTargetAsync(Guid runtimeId, string targetId, IReadOnlyList<RuntimePublishedPortView> ports, CancellationToken ct)
    {
        if (targetId.Length != 64 || !targetId.All(char.IsAsciiHexDigit) || ports.Count is 0 or > 64) return false;
        ContainerInspectResponse inspected;
        try { inspected = await docker.Containers.InspectContainerAsync(targetId, ct); }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { return false; }
        if (inspected.State?.Running != true || inspected.Config?.Labels is not { } labels
            || !labels.TryGetValue("noctf.io/runtime-instance-id", out var identity)
            || !Guid.TryParse(identity, out var parsed) || parsed != runtimeId) return false;
        return ports.All(port => port.ServiceName is null && port.ContainerPort is > 0 and <= 65535
            && inspected.NetworkSettings?.Ports?.TryGetValue($"{port.ContainerPort}/tcp", out var bindings) == true
            && bindings is not null && bindings.Count > 0
            && bindings.All(binding => binding.HostPort == port.HostPort.ToString(CultureInfo.InvariantCulture)));
    }

    public async Task<GatewayPublication> StartAsync(Guid runtimeId, string targetId, IReadOnlyList<RuntimePublishedPortView> ports, CancellationToken ct)
    {
        if (!await VerifyTargetAsync(runtimeId, targetId, ports, ct)) throw new InvalidOperationException("Gateway target identity check failed.");
        var id = Guid.NewGuid();
        var publicationName = $"noctf-gateway-{id:N}";
        var created = await docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = publicationName,
            Image = options.HelperImage,
            User = "65532:65532",
            Env = [$"NOCTF_PUBLICATION_ID={id}"],
            Labels = new Dictionary<string, string>
            {
                [ConnectorLabel] = options.ConnectorId,
                ["noctf.io/public-gateway-runner"] = options.RunnerId,
                ["noctf.io/publication-id"] = id.ToString(),
                ["noctf.io/publication-runtime"] = runtimeId.ToString(),
                ["noctf.io/publication-target"] = targetId
            },
            HostConfig = new HostConfig
            {
                NetworkMode = "container:" + targetId,
                Memory = 32 * 1024 * 1024,
                NanoCPUs = 100_000_000,
                PidsLimit = 64,
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No },
                LogConfig = new LogConfig { Type = "local", Config = new Dictionary<string, string> { ["max-size"] = "1m", ["max-file"] = "2" } }
            }
        }, ct);
        var adminPort = RandomNumberGenerator.GetInt32(61000, 65535);
        while (ports.Any(port => port.ContainerPort == adminPort)) adminPort = RandomNumberGenerator.GetInt32(61000, 65535);
        var publication = new GatewayPublication(id, runtimeId, targetId, created.ID, ports, adminPort);
        try
        {
            var adminPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            using var key = RSA.Create(2048);
            var certificateRequest = new CertificateRequest("CN=NoCTF private FRP status", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var alternativeNames = new SubjectAlternativeNameBuilder(); alternativeNames.AddIpAddress(IPAddress.Loopback);
            certificateRequest.CertificateExtensions.Add(alternativeNames.Build());
            using var certificate = certificateRequest.CreateSelfSigned(clock.GetUtcNow().AddMinutes(-1), clock.GetUtcNow().AddDays(7));
            var files = new Dictionary<string, byte[]>
            {
                ["frpc.toml"] = Encoding.UTF8.GetBytes(await ConfigurationAsync(publication, adminPassword, ct)),
                ["admin.json"] = JsonSerializer.SerializeToUtf8Bytes(new { port = adminPort, password = adminPassword }),
                ["admin.crt"] = Encoding.UTF8.GetBytes(certificate.ExportCertificatePem()),
                ["admin.key"] = Encoding.UTF8.GetBytes(key.ExportPkcs8PrivateKeyPem()),
                ["ca.crt"] = await File.ReadAllBytesAsync(options.CaFile, ct),
                ["client.crt"] = await File.ReadAllBytesAsync(options.CertificateFile, ct),
                ["client.key"] = await File.ReadAllBytesAsync(options.KeyFile, ct)
            };
            await WriteFilesAsync(publication.HelperId, files, ct);
            // The coordinator must issue a fresh lease only after rechecking database eligibility.
            return publication;
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await RevokeAsync(publication, cleanup.Token);
            throw;
        }
    }

    public async Task RenewAsync(GatewayPublication publication, DateTimeOffset expiresAt, CancellationToken ct)
    {
        if (!await VerifyTargetAsync(publication.RuntimeId, publication.TargetId, publication.Ports, ct))
            throw new InvalidOperationException("Gateway target is no longer the approved running resource.");
        var helper = await docker.Containers.InspectContainerAsync(publication.HelperId, ct);
        if (!Owns(helper, publication)) throw new InvalidOperationException("Gateway publication identity mismatch.");
        if (helper.State?.Status is not ("created" or "running"))
            throw new InvalidOperationException("Expired publications must be recreated with a new identity.");
        await WriteFilesAsync(publication.HelperId, new Dictionary<string, byte[]>
        {
            [helper.State?.Running == true ? "lease.next" : "lease.json"] = JsonSerializer.SerializeToUtf8Bytes(new { publicationId = publication.Id.ToString(), expiresAtUnixMs = expiresAt.ToUnixTimeMilliseconds() })
        }, ct);
        if (helper.State?.Running == true)
        {
            // A fixed local rename avoids the supervisor observing a half-written lease.
            var move = await docker.Exec.CreateContainerExecAsync(publication.HelperId, new ContainerExecCreateParameters
            { Cmd = ["mv", "/run/noctf-gateway/lease.next", "/run/noctf-gateway/lease.json"] }, ct);
            using var output = await docker.Exec.StartContainerExecAsync(move.ID, new ContainerExecStartParameters { Detach = false, TTY = false }, ct);
            while (true)
            {
                var result = await docker.Exec.InspectContainerExecAsync(move.ID, ct);
                if (!result.Running)
                {
                    if (result.ExitCode != 0) throw new InvalidOperationException("Gateway lease could not be atomically applied.");
                    break;
                }
                await Task.Delay(20, ct);
            }
        }
        else
            await docker.Containers.StartContainerAsync(publication.HelperId, new ContainerStartParameters(), ct);
    }

    public async Task<IReadOnlySet<int>> ReadyPortsAsync(GatewayPublication publication, CancellationToken ct)
        => (await ReadStatusAsync(publication, ct)).Where(item => item.State == PublicAccessState.Ready).Select(item => item.ContainerPort).ToHashSet();

    public async Task<IReadOnlyList<PublicEndpointStatus>> ReadStatusAsync(GatewayPublication publication, CancellationToken ct)
    {
        var helper = await docker.Containers.InspectContainerAsync(publication.HelperId, ct);
        if (!Owns(helper, publication) || helper.State?.Running != true) return [];
        var probe = await docker.Exec.CreateContainerExecAsync(publication.HelperId, new ContainerExecCreateParameters
        { Cmd = ["python", "/app/status_probe.py"], AttachStdout = true, AttachStderr = false }, ct);
        using var output = await docker.Exec.StartContainerExecAsync(probe.ID, new ContainerExecStartParameters { Detach = false, TTY = false }, ct);
        var (stdout, _) = await output.ReadOutputToEndAsync(ct);
        if (stdout.Length > 8192) return [];
        var entries = JsonSerializer.Deserialize<ProxyStatus[]>(stdout, JsonOptions) ?? [];
        return publication.Ports.Select(port =>
        {
            var status = entries.FirstOrDefault(item => item.Name == ProxyName(publication.Id, port.ContainerPort))?.State;
            return status switch
            {
                "running" => new PublicEndpointStatus(port.ContainerPort, port.HostPort, PublicAccessState.Ready, null, port.HostPort),
                "start error" or "closed" => new PublicEndpointStatus(port.ContainerPort, port.HostPort, PublicAccessState.Unavailable, PublicAccessFailure.PublicPortUnavailable),
                _ => new PublicEndpointStatus(port.ContainerPort, port.HostPort, PublicAccessState.Pending, PublicAccessFailure.GatewayReconciliationPending)
            };
        }).ToArray();
    }

    public async Task RevokeAsync(GatewayPublication publication, CancellationToken ct)
    {
        try
        {
            var helper = await docker.Containers.InspectContainerAsync(publication.HelperId, ct);
            if (!Owns(helper, publication)) throw new InvalidOperationException("Refusing to revoke a foreign gateway resource.");
            await docker.Containers.RemoveContainerAsync(publication.HelperId, new ContainerRemoveParameters { Force = true }, ct);
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { }
    }

    public async Task RevokeStaleHelpersAsync(CancellationToken ct)
    {
        var helpers = await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>> { ["label"] = new Dictionary<string, bool> { [$"{ConnectorLabel}={options.ConnectorId}"] = true } }
        }, ct);
        foreach (var helper in helpers.OrderBy(item => Label(item.Labels, "noctf.io/gateway-role") == "client" ? 0 : 1).Take(256))
        {
            var labels = helper.Labels;
            if (labels is null || Label(labels, "noctf.io/public-gateway-runner") != options.RunnerId) continue;
            if (Guid.TryParse(Label(labels, "noctf.io/gateway-session"), out var session))
            {
                // Switching back from the managed SSH transport in the same deployment. Only reserved
                // gateway labels, generated names and (for relays) immutable target bindings qualify.
                var inspected = await docker.Containers.InspectContainerAsync(helper.ID, ct);
                var role = Label(labels, "noctf.io/gateway-role");
                var name = inspected.Name?.TrimStart('/') ?? "";
                var knownClient = role == "client" && name.StartsWith($"noctf-ssh-{session:N}-", StringComparison.Ordinal);
                var knownRelay = role == "relay" && Guid.TryParse(Label(labels, "noctf.io/publication-id"), out var publication)
                    && name == $"noctf-relay-{publication:N}" && Label(labels, "noctf.io/publication-target") is { Length: 64 } target
                    && inspected.HostConfig?.NetworkMode == "container:" + target;
                if (knownClient || knownRelay)
                    await docker.Containers.RemoveContainerAsync(helper.ID, new ContainerRemoveParameters { Force = true }, ct);
                continue;
            }
            if (!Guid.TryParse(Label(labels, "noctf.io/publication-id"), out var id)
                || !Guid.TryParse(Label(labels, "noctf.io/publication-runtime"), out var runtimeId)
                || Label(labels, "noctf.io/publication-target") is not { } targetId) continue;
            await RevokeAsync(new(id, runtimeId, targetId, helper.ID, []), ct);
        }
    }

    private bool Owns(ContainerInspectResponse helper, GatewayPublication publication) =>
        helper.Config is { } config && ApprovedImage(config.Image) && helper.HostConfig?.NetworkMode == "container:" + publication.TargetId
        && Label(config.Labels, ConnectorLabel) == options.ConnectorId
        && Label(config.Labels, "noctf.io/public-gateway-runner") == options.RunnerId
        && Label(config.Labels, "noctf.io/publication-id") == publication.Id.ToString()
        && Label(config.Labels, "noctf.io/publication-runtime") == publication.RuntimeId.ToString();

    private static string? Label(IDictionary<string, string>? labels, string key) =>
        labels is not null && labels.TryGetValue(key, out var value) ? value : null;
    private bool ApprovedImage(string? image) => image == options.HelperImage || image is not null
        && options.HelperImage.Contains("@sha256:", StringComparison.Ordinal)
        && image.StartsWith(options.HelperImage.Split('@')[0] + "@sha256:", StringComparison.Ordinal)
        && image.Split('@')[1].Length == 71 && image.Split('@')[1][7..].All(char.IsAsciiHexDigit);
    private sealed record ProxyStatus(string Name, string State);

    private static string ProxyName(Guid publicationId, int port) => $"noctf-{publicationId:N}-{port}";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private async Task<string> ConfigurationAsync(GatewayPublication publication, string adminPassword, CancellationToken ct)
    {
        var token = (await File.ReadAllTextAsync(options.TokenFile, ct)).Trim();
        if (token.Length is < 32 or > 512) throw new InvalidOperationException("Gateway pairing token length is invalid.");
        string Quote(string value) => JsonSerializer.Serialize(value);
        var config = new StringBuilder($$"""
            serverAddr = {{Quote(options.ServerHost)}}
            serverPort = {{options.ServerPort}}
            auth.method = "token"
            auth.token = {{Quote(token)}}
            transport.heartbeatInterval = 1
            transport.heartbeatTimeout = 3
            transport.tls.enable = true
            transport.tls.serverName = {{Quote(options.ServerName)}}
            transport.tls.trustedCaFile = "/run/noctf-gateway/ca.crt"
            transport.tls.certFile = "/run/noctf-gateway/client.crt"
            transport.tls.keyFile = "/run/noctf-gateway/client.key"
            webServer.addr = "127.0.0.1"
            webServer.port = {{publication.AdminPort}}
            webServer.user = "noctf"
            webServer.password = {{Quote(adminPassword)}}
            webServer.tls.certFile = "/run/noctf-gateway/admin.crt"
            webServer.tls.keyFile = "/run/noctf-gateway/admin.key"

            """);
        foreach (var port in publication.Ports)
            config.AppendLine($$"""
                [[proxies]]
                name = "{{ProxyName(publication.Id, port.ContainerPort)}}"
                type = "tcp"
                localIP = "127.0.0.1"
                localPort = {{port.ContainerPort}}
                remotePort = {{port.HostPort}}
                """);
        return config.ToString();
    }

    private async Task WriteFilesAsync(string helperId, IReadOnlyDictionary<string, byte[]> files, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        using (var tar = new TarWriter(stream, TarEntryFormat.Ustar, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = new UstarTarEntry(TarEntryType.RegularFile, name)
                {
                    Uid = 65532, Gid = 65532, Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    DataStream = new MemoryStream(content, writable: false)
                };
                await tar.WriteEntryAsync(entry, ct);
                entry.DataStream.Dispose();
            }
        }
        stream.Position = 0;
        await docker.Containers.ExtractArchiveToContainerAsync(helperId,
            new CopyToContainerParameters { Path = "/run/noctf-gateway" }, stream, ct);
    }
    public void Dispose() => docker.Dispose();
}

using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace NoCTF.Tests.Integration.Runtime;

/// <summary>
/// Stage A negative controls, NOT a production gateway implementation.
/// A passing negative control proves the candidate is unsafe for unmanaged Docker removal.
/// </summary>
[Category("Integration"), Category("GatewaySafetyPrototype"), NotInParallel]
public sealed class PublicGatewaySafetyPrototypeTests
{
    private const string Image = "m.daocloud.io/docker.io/library/python:3.13-alpine@sha256:540c7d91f98ff6880174c40e99067bf5941eb54d818a7a5e094d188b196a934d";
    private const string ArchiveHash = "3CF934477F4FB1EE9E19E49C31FB33F5FFE3283300076F59AFAD8B8CCF1E1621";
    private static readonly Lazy<Task<Dictionary<string, byte[]>>> Binaries = new(ReadBinariesAsync);

    [Test, Arguments(false, false), Arguments(true, false), Arguments(false, true), Arguments(true, true)]
    [Timeout(120_000)]
    public async Task Port_reuse_negative_control_and_explicit_revoke_boundary(
        bool unixSocket, bool revokeFirst, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            var binaries = await Binaries.Value.WaitAsync(ct);
            var tls = CreateCertificates();
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await using var network = new NetworkBuilder().Build();
            await network.CreateAsync(ct);
            await using var targetA = Target("runtime-A").WithPortBinding(8080, true).Build();
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
                : $"localIP = \"host.docker.internal\"\nlocalPort = {localPort}";
            await using var client = new ContainerBuilder(Image).WithNetwork(network)
                .WithExtraHost("host.docker.internal", "host-gateway")
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
                    """), "/frpc.toml")
                .WithEntrypoint("/bin/sh", "-c", unixSocket
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
            if (revokeFirst)
                await client.StopAsync(ct);

            // Only this test-owned resource is removed. B deliberately takes A's released host port.
            await targetA.DisposeAsync();
            await using var targetB = Target("runtime-B").WithPortBinding(localPort, 8080).Build();
            await targetB.StartAsync(ct);
            if (revokeFirst)
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

    private static (byte[] Ca, byte[] ServerCert, byte[] ServerKey, byte[] ClientCert, byte[] ClientKey) CreateCertificates()
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

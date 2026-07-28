using System.Formats.Tar;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Runtime.Libvirt;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
[NotInParallel]
public sealed class LibvirtOvaRuntimeIntegrationTests
{
    private const string DiskPathEnvironmentVariable = "NOCTF_LIBVIRT_DISK_PATH";

    [Test]
    [Timeout(600_000)]
    public async Task Real_libvirt_import_exposes_guest_url_and_cleans_exact_identity(
        CancellationToken cancellationToken)
    {
        var diskPath = Environment.GetEnvironmentVariable(DiskPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(diskPath))
        {
            Skip.Test(
                $"Integration skipped: {DiskPathEnvironmentVariable} is not configured.");
        }
        diskPath = Path.GetFullPath(diskPath);
        if (!File.Exists(diskPath))
        {
            throw new InvalidOperationException(
                $"{DiskPathEnvironmentVariable} does not reference an existing disk image.");
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "noctf-libvirt-integration",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var operationId = Guid.NewGuid();
        var initialIdentity = new OvaManagedRuntimeResource(operationId, 0);
        var replacementIdentity = new OvaManagedRuntimeResource(operationId, 1);
        var ovaPath = Path.Combine(root, "appliance.ova");
        var options = new LibvirtRuntimeOptions(
            Path.Combine(root, "cache"),
            Path.Combine(root, "work"),
            "10.253.0.0/16",
            "10.253.240.0/24",
            28);
        var processes = new LibvirtProcessAdapter();
        var runtime = new LibvirtApplianceLifecycle(
            processes,
            new OvaArtifactCache(new HttpClient(), options),
            new LibvirtRoutedNetworkManager(processes, options),
            options,
            TimeProvider.System);

        try
        {
            await WriteSingleVmOvaAsync(ovaPath, diskPath, cancellationToken);
            var digest = await Sha256Async(ovaPath, cancellationToken);
            var receipt = await ImportAsync(
                runtime,
                ovaPath,
                digest,
                operationId,
                initialIdentity.Generation,
                cancellationToken);

            await Assert.That(receipt.VirtualMachines).HasSingleItem();
            var machine = receipt.VirtualMachines.Single();
            await Assert.That(IPAddress.TryParse(machine.Address, out _)).IsTrue();
            await Assert.That(receipt.NetworkCidr).StartsWith("10.253.240.");
            await Assert.That(await runtime.ListManagedAsync(cancellationToken))
                .Contains(initialIdentity);

            await StartGuestHttpServerAsync(
                processes,
                machine.ResourceId,
                cancellationToken);
            await WaitForGuestHttpAsync(machine.Address, cancellationToken);

            await runtime.DestroyAsync(receipt, cancellationToken);
            await Assert.That(await runtime.ListManagedAsync(cancellationToken))
                .DoesNotContain(initialIdentity);

            var replacement = await ImportAsync(
                runtime,
                ovaPath,
                digest,
                operationId,
                replacementIdentity.Generation,
                cancellationToken);
            await Assert.That(replacement.VirtualMachines).HasSingleItem();
            await Assert.That(IPAddress.TryParse(
                    replacement.VirtualMachines.Single().Address,
                    out _))
                .IsTrue();
            await Assert.That(await runtime.ListManagedAsync(cancellationToken))
                .Contains(replacementIdentity);

            await runtime.DestroyByIdentityAsync(replacementIdentity, cancellationToken);
            await Assert.That(await runtime.ListManagedAsync(cancellationToken))
                .DoesNotContain(replacementIdentity);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            await runtime.DestroyByIdentityAsync(initialIdentity, cleanup.Token);
            await runtime.DestroyByIdentityAsync(replacementIdentity, cleanup.Token);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static Task<OvaRuntimeReceipt> ImportAsync(
        IOvaRuntime runtime,
        string ovaPath,
        string digest,
        Guid operationId,
        int generation,
        CancellationToken cancellationToken) =>
        runtime.ImportAsync(
            new OvaRuntimeRequest(
                operationId,
                generation,
                new Uri(ovaPath),
                digest,
                $"noctf-{operationId:N}-{generation}",
                new(512 * 1024 * 1024, 1_000_000_000, 256),
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(5)),
            cancellationToken);

    private static async Task WriteSingleVmOvaAsync(
        string ovaPath,
        string diskPath,
        CancellationToken cancellationToken)
    {
        const string descriptor = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ovf:Envelope
                xmlns:ovf="http://schemas.dmtf.org/ovf/envelope/1"
                xmlns:rasd="http://schemas.dmtf.org/wbem/wscim/1/cim-schema/2/CIM_ResourceAllocationSettingData">
              <ovf:References>
                <ovf:File ovf:id="disk-file" ovf:href="disk.img" />
              </ovf:References>
              <ovf:DiskSection>
                <ovf:Info>Disks</ovf:Info>
                <ovf:Disk ovf:diskId="disk" ovf:fileRef="disk-file" />
              </ovf:DiskSection>
              <ovf:VirtualSystem ovf:id="web">
                <ovf:Info>Libvirt integration fixture</ovf:Info>
                <ovf:VirtualHardwareSection>
                  <ovf:Info>Hardware</ovf:Info>
                  <ovf:Item>
                    <rasd:ResourceType>3</rasd:ResourceType>
                    <rasd:VirtualQuantity>1</rasd:VirtualQuantity>
                  </ovf:Item>
                  <ovf:Item>
                    <rasd:ResourceType>4</rasd:ResourceType>
                    <rasd:AllocationUnits>byte * 2^20</rasd:AllocationUnits>
                    <rasd:VirtualQuantity>512</rasd:VirtualQuantity>
                  </ovf:Item>
                  <ovf:Item>
                    <rasd:ResourceType>17</rasd:ResourceType>
                    <rasd:HostResource>ovf:/disk/disk</rasd:HostResource>
                  </ovf:Item>
                </ovf:VirtualHardwareSection>
              </ovf:VirtualSystem>
            </ovf:Envelope>
            """;

        await using var archive = File.Create(ovaPath);
        using var writer = new TarWriter(archive, leaveOpen: false);
        await using (var descriptorData = new MemoryStream(Encoding.UTF8.GetBytes(descriptor)))
        {
            await writer.WriteEntryAsync(
                new PaxTarEntry(TarEntryType.RegularFile, "appliance.ovf")
                {
                    DataStream = descriptorData
                },
                cancellationToken);
        }
        await using var disk = File.OpenRead(diskPath);
        await writer.WriteEntryAsync(
            new PaxTarEntry(TarEntryType.RegularFile, "disk.img")
            {
                DataStream = disk
            },
            cancellationToken);
    }

    private static async Task StartGuestHttpServerAsync(
        ILibvirtProcessAdapter processes,
        string domainName,
        CancellationToken cancellationToken)
    {
        const string command =
            """{"execute":"guest-exec","arguments":{"path":"/usr/bin/python3","arg":["-m","http.server","8080","--bind","0.0.0.0"],"capture-output":false}}""";
        var result = await processes.RunAsync(
            "virsh",
            ["qemu-agent-command", domainName, command],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"QEMU Guest Agent could not start the fixture HTTP server: {result.StandardError}");
        }
    }

    private static async Task WaitForGuestHttpAsync(
        string address,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(
                    $"http://{address}:8080/",
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException)
            {
                // The guest process or route is not ready yet.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Retry the per-request timeout until the overall deadline.
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        throw new TimeoutException("The imported Libvirt guest URL did not become reachable.");
    }

    private static async Task<string> Sha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(
            await SHA256.HashDataAsync(stream, cancellationToken));
    }
}

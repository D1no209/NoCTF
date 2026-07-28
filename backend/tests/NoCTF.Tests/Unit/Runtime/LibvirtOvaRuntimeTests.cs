using System.Formats.Tar;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Runtime.Libvirt;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class LibvirtOvaRuntimeTests
{
    [Test]
    public async Task Multi_vm_ova_is_imported_with_stable_ids_addresses_and_idempotent_cleanup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var ovaPath = Path.Combine(root, "appliance.ova");
            await WriteApplianceAsync(ovaPath);
            var digest = await Sha256Async(ovaPath);
            var options = new LibvirtRuntimeOptions(
                Path.Combine(root, "cache"),
                Path.Combine(root, "work"),
                "10.90.0.0/16",
                "10.90.0.0/24",
                28);
            var adapter = new RecordingLibvirtProcessAdapter();
            var runtime = new LibvirtApplianceLifecycle(
                adapter,
                new OvaArtifactCache(new HttpClient(), options),
                new LibvirtRoutedNetworkManager(adapter, options),
                options,
                TimeProvider.System);
            var operationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var request = new OvaRuntimeRequest(
                operationId,
                3,
                new Uri(ovaPath),
                digest,
                $"noctf-{operationId:N}-3",
                new(256 * 1024 * 1024, 2_000_000_000, 256),
                TimeSpan.FromHours(1),
                TimeSpan.FromSeconds(5));

            var receipt = await runtime.ImportAsync(request, CancellationToken.None);

            await Assert.That(receipt.Provider)
                .IsEqualTo(NoCTF.Domain.Runtime.RuntimeProvider.Libvirt);
            await Assert.That(receipt.VirtualMachines.Select(machine => machine.VmId))
                .IsEquivalentTo(["web", "db"]);
            await Assert.That(receipt.VirtualMachines.All(machine =>
                    IPAddress.TryParse(machine.Address, out _)))
                .IsTrue();
            await Assert.That(adapter.CreatedDomains.Count).IsEqualTo(2);
            await Assert.That(adapter.NetworkExists).IsTrue();
            await Assert.That(await runtime.ListManagedAsync(CancellationToken.None))
                .IsEquivalentTo([new OvaManagedRuntimeResource(operationId, 3)]);

            var unsafeCleanup = () => runtime.DestroyAsync(
                receipt with { NetworkId = "default" },
                CancellationToken.None);
            await Assert.That(unsafeCleanup).Throws<InvalidOperationException>();
            await Assert.That(adapter.NetworkExists).IsTrue();

            await runtime.DestroyByIdentityAsync(
                new(operationId, 3),
                CancellationToken.None);
            await runtime.DestroyAsync(receipt, CancellationToken.None);
            await runtime.DestroyAsync(receipt, CancellationToken.None);

            await Assert.That(adapter.CreatedDomains).IsEmpty();
            await Assert.That(adapter.NetworkExists).IsFalse();
            await Assert.That(Directory.Exists(
                    Path.Combine(options.WorkDirectory, $"{operationId:N}-3")))
                .IsFalse();
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Test]
    public async Task Ova_digest_mismatch_is_rejected_before_extraction()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var ovaPath = Path.Combine(root, "appliance.ova");
            await File.WriteAllTextAsync(ovaPath, "not-the-declared-appliance");
            var options = new LibvirtRuntimeOptions(
                Path.Combine(root, "cache"),
                Path.Combine(root, "work"),
                "10.91.0.0/16",
                "10.91.0.0/24",
                28);
            var cache = new OvaArtifactCache(new HttpClient(), options);

            var action = async () =>
            {
                _ = await cache.ResolveAsync(
                    new Uri(ovaPath),
                    new string('a', 64),
                    CancellationToken.None);
            };

            await Assert.That(action).Throws<InvalidOperationException>();
            await Assert.That(Directory.EnumerateFiles(options.CacheDirectory)).IsEmpty();
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Test]
    public async Task Libvirt_process_stderr_is_preserved_for_node_diagnostics()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var ovaPath = Path.Combine(root, "appliance.ova");
            await WriteApplianceAsync(ovaPath);
            var digest = await Sha256Async(ovaPath);
            var options = new LibvirtRuntimeOptions(
                Path.Combine(root, "cache"),
                Path.Combine(root, "work"),
                "10.92.0.0/16",
                "10.92.0.0/24",
                28);
            const string processError = "iptables-nft table is incompatible";
            var adapter = new RecordingLibvirtProcessAdapter
            {
                FailingCommand = "net-start",
                FailureMessage = processError
            };
            var runtime = new LibvirtApplianceLifecycle(
                adapter,
                new OvaArtifactCache(new HttpClient(), options),
                new LibvirtRoutedNetworkManager(adapter, options),
                options,
                TimeProvider.System);
            var operationId = Guid.NewGuid();
            var action = async () =>
            {
                _ = await runtime.ImportAsync(
                    new OvaRuntimeRequest(
                        operationId,
                        0,
                        new Uri(ovaPath),
                        digest,
                        $"noctf-{operationId:N}-0",
                        new(256 * 1024 * 1024, 2_000_000_000, 256),
                        TimeSpan.FromMinutes(1),
                        TimeSpan.FromSeconds(5)),
                    CancellationToken.None);
            };

            var exception = await Assert.That(action).Throws<InvalidOperationException>();

            await Assert.That(exception!.Message).Contains(processError);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Test]
    public async Task Ovf_aggregate_resources_cannot_exceed_runtime_budget()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var ovaPath = Path.Combine(root, "appliance.ova");
            var extraction = Path.Combine(root, "extracted");
            await WriteApplianceAsync(ovaPath);
            var archive = await OvaArchiveExtractor.ExtractAsync(
                ovaPath,
                extraction,
                CancellationToken.None);

            var action = () => OvfApplianceParser.Parse(
                archive,
                new RuntimeResourceLimits(
                    255 * 1024 * 1024,
                    2_000_000_000,
                    256));

            await Assert.That(action).Throws<InvalidOperationException>();
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Test]
    public async Task Ova_archive_rejects_path_traversal()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var ovaPath = Path.Combine(root, "traversal.ova");
            await using (var stream = File.Create(ovaPath))
            using (var writer = new TarWriter(stream, leaveOpen: false))
            {
                await WriteTarEntryAsync(writer, "../outside.ovf", "<Envelope />");
            }

            var action = async () =>
            {
                _ = await OvaArchiveExtractor.ExtractAsync(
                    ovaPath,
                    Path.Combine(root, "extracted"),
                    CancellationToken.None);
            };

            await Assert.That(action).Throws<InvalidOperationException>();
            await Assert.That(File.Exists(Path.Combine(root, "outside.ovf"))).IsFalse();
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    private static async Task WriteApplianceAsync(string path)
    {
        const string descriptor = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ovf:Envelope
                xmlns:ovf="http://schemas.dmtf.org/ovf/envelope/1"
                xmlns:rasd="http://schemas.dmtf.org/wbem/wscim/1/cim-schema/2/CIM_ResourceAllocationSettingData">
              <ovf:References>
                <ovf:File ovf:id="web-file" ovf:href="web.vmdk" />
                <ovf:File ovf:id="db-file" ovf:href="db.vmdk" />
              </ovf:References>
              <ovf:DiskSection>
                <ovf:Info>Disks</ovf:Info>
                <ovf:Disk ovf:diskId="web-disk" ovf:fileRef="web-file" />
                <ovf:Disk ovf:diskId="db-disk" ovf:fileRef="db-file" />
              </ovf:DiskSection>
              <ovf:VirtualSystemCollection ovf:id="challenge">
                <ovf:Info>Challenge appliance</ovf:Info>
                <ovf:VirtualSystem ovf:id="web">
                  <ovf:Info>Web</ovf:Info>
                  <ovf:VirtualHardwareSection>
                    <ovf:Info>Hardware</ovf:Info>
                    <ovf:Item>
                      <rasd:ResourceType>3</rasd:ResourceType>
                      <rasd:VirtualQuantity>1</rasd:VirtualQuantity>
                    </ovf:Item>
                    <ovf:Item>
                      <rasd:ResourceType>4</rasd:ResourceType>
                      <rasd:AllocationUnits>byte * 2^20</rasd:AllocationUnits>
                      <rasd:VirtualQuantity>128</rasd:VirtualQuantity>
                    </ovf:Item>
                    <ovf:Item>
                      <rasd:ResourceType>17</rasd:ResourceType>
                      <rasd:HostResource>ovf:/disk/web-disk</rasd:HostResource>
                    </ovf:Item>
                  </ovf:VirtualHardwareSection>
                </ovf:VirtualSystem>
                <ovf:VirtualSystem ovf:id="db">
                  <ovf:Info>Database</ovf:Info>
                  <ovf:VirtualHardwareSection>
                    <ovf:Info>Hardware</ovf:Info>
                    <ovf:Item>
                      <rasd:ResourceType>3</rasd:ResourceType>
                      <rasd:VirtualQuantity>1</rasd:VirtualQuantity>
                    </ovf:Item>
                    <ovf:Item>
                      <rasd:ResourceType>4</rasd:ResourceType>
                      <rasd:AllocationUnits>byte * 2^20</rasd:AllocationUnits>
                      <rasd:VirtualQuantity>128</rasd:VirtualQuantity>
                    </ovf:Item>
                    <ovf:Item>
                      <rasd:ResourceType>17</rasd:ResourceType>
                      <rasd:HostResource>ovf:/disk/db-disk</rasd:HostResource>
                    </ovf:Item>
                  </ovf:VirtualHardwareSection>
                </ovf:VirtualSystem>
              </ovf:VirtualSystemCollection>
            </ovf:Envelope>
            """;
        await using var stream = File.Create(path);
        using var writer = new TarWriter(stream, leaveOpen: false);
        await WriteTarEntryAsync(writer, "appliance.ovf", descriptor);
        await WriteTarEntryAsync(writer, "web.vmdk", "web-disk");
        await WriteTarEntryAsync(writer, "db.vmdk", "db-disk");
    }

    private static async Task WriteTarEntryAsync(
        TarWriter writer,
        string name,
        string content)
    {
        await using var data = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = data
        };
        await writer.WriteEntryAsync(entry, CancellationToken.None);
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(
            await SHA256.HashDataAsync(stream, CancellationToken.None));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "noctf-libvirt-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class RecordingLibvirtProcessAdapter : ILibvirtProcessAdapter
    {
        private string? networkXml;
        private string? networkName;

        public string? FailingCommand { get; init; }
        public string? FailureMessage { get; init; }
        public HashSet<string> CreatedDomains { get; } = new(StringComparer.Ordinal);
        public bool NetworkExists { get; private set; }

        public Task<LibvirtProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = executable == "virsh" ? arguments[0] : executable;
            if (string.Equals(command, FailingCommand, StringComparison.Ordinal))
                return Failure(FailureMessage ?? "forced failure");
            if (executable == "qemu-img")
            {
                File.Copy(arguments[^2], arguments[^1]);
                return Success();
            }
            if (executable == "virt-install")
            {
                var nameIndex = Array.IndexOf(arguments.ToArray(), "--name");
                CreatedDomains.Add(arguments[nameIndex + 1]);
                return Success();
            }
            if (executable != "virsh")
                throw new InvalidOperationException($"Unexpected executable '{executable}'.");

            return arguments[0] switch
            {
                "net-list" => Success(NetworkExists ? $"{networkName}\n" : string.Empty),
                "net-dumpxml" => NetworkExists
                    ? Success(networkXml!)
                    : Failure(),
                "net-define" => DefineNetwork(arguments[1]),
                "net-start" => StartNetwork(),
                "net-destroy" => Success(),
                "net-undefine" => UndefineNetwork(),
                "list" => Success(string.Join('\n', CreatedDomains)),
                "dominfo" => CreatedDomains.Contains(arguments[1])
                    ? Success()
                    : Failure(),
                "domstate" => CreatedDomains.Contains(arguments[1])
                    ? Success("running\n")
                    : Failure(),
                "domifaddr" => Success(AddressOutput(arguments[1])),
                "start" => Success(),
                "destroy" => Success(),
                "undefine" => UndefineDomain(arguments[1]),
                _ => throw new InvalidOperationException(
                    $"Unexpected virsh command '{arguments[0]}'.")
            };
        }

        private Task<LibvirtProcessResult> DefineNetwork(string path)
        {
            networkXml = File.ReadAllText(path);
            networkName = XDocument.Parse(networkXml).Root!.Element("name")!.Value;
            return Success();
        }

        private Task<LibvirtProcessResult> StartNetwork()
        {
            NetworkExists = true;
            return Success();
        }

        private Task<LibvirtProcessResult> UndefineNetwork()
        {
            NetworkExists = false;
            networkXml = null;
            networkName = null;
            return Success();
        }

        private Task<LibvirtProcessResult> UndefineDomain(string name)
        {
            CreatedDomains.Remove(name);
            return Success();
        }

        private string AddressOutput(string domainName)
        {
            var gatewayText = XDocument.Parse(networkXml!)
                .Root!
                .Element("ip")!
                .Attribute("address")!
                .Value;
            var bytes = IPAddress.Parse(gatewayText).GetAddressBytes();
            bytes[^1] = checked((byte)(bytes[^1]
                + 1
                + int.Parse(domainName[^3..], System.Globalization.CultureInfo.InvariantCulture)));
            return $"vnet0 52:54:00:00:00:01 ipv4 {new IPAddress(bytes)}/28\n";
        }

        private static Task<LibvirtProcessResult> Success(string output = "") =>
            Task.FromResult(new LibvirtProcessResult(0, output, string.Empty));

        private static Task<LibvirtProcessResult> Failure(string message = "not found") =>
            Task.FromResult(new LibvirtProcessResult(1, string.Empty, message));
    }
}

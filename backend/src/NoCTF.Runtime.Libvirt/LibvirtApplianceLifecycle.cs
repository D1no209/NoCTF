using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runtime.Libvirt;

public sealed partial class LibvirtApplianceLifecycle(
    ILibvirtProcessAdapter processes,
    OvaArtifactCache cache,
    LibvirtRoutedNetworkManager networks,
    LibvirtRuntimeOptions options,
    TimeProvider timeProvider) : IOvaRuntime
{
    private static readonly TimeSpan AddressPollInterval = TimeSpan.FromSeconds(1);

    public async Task<OvaRuntimeReceipt> ImportAsync(
        OvaRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var runtimeDirectory = RuntimeDirectory(request.OperationId, request.Generation);
        var extractionDirectory = Path.Combine(runtimeDirectory, "ova");
        RecreateExtractionDirectory(extractionDirectory);

        LibvirtRoutedNetwork? network = null;
        IReadOnlyList<OvfVirtualMachinePlan>? plans = null;
        try
        {
            var ovaPath = await cache.ResolveAsync(
                request.OvaSource,
                request.Sha256,
                cancellationToken);
            var archive = await OvaArchiveExtractor.ExtractAsync(
                ovaPath,
                extractionDirectory,
                cancellationToken);
            var appliance = OvfApplianceParser.Parse(archive, request.Limits);
            plans = appliance.VirtualMachines;
            var maximumGuests =
                (1L << (32 - options.RuntimeSubnetPrefixLength)) - 3;
            if (plans.Count > Math.Min(maximumGuests, 1000))
                throw new InvalidOperationException(
                    "OVA contains more virtual machines than the Runtime identity or subnet can address.");
            network = await networks.EnsureAsync(
                request.OperationId,
                request.Generation,
                request.NetworkName,
                cancellationToken);

            var machines = new List<OvaVirtualMachineReceipt>(plans.Count);
            for (var index = 0; index < plans.Count; index++)
            {
                var plan = plans[index];
                var domainName = DomainName(request.OperationId, request.Generation, index);
                await EnsureDomainAsync(
                    domainName,
                    plan,
                    index,
                    request.NetworkName,
                    runtimeDirectory,
                    cancellationToken);
                machines.Add(new(plan.VmId, domainName, string.Empty));
            }

            var deadline = timeProvider.GetUtcNow().Add(request.OperationTimeout);
            var addresses = new HashSet<IPAddress>();
            for (var index = 0; index < machines.Count; index++)
            {
                var address = await DiscoverAddressAsync(
                    machines[index].ResourceId,
                    network.Subnet,
                    deadline,
                    cancellationToken);
                if (!addresses.Add(address))
                    throw new InvalidOperationException(
                        "QEMU Guest Agent reported a duplicate appliance address.");
                machines[index] = machines[index] with { Address = address.ToString() };
            }

            DeleteDirectoryIfPresent(extractionDirectory);
            return new(
                request.OperationId,
                RuntimeProvider.Libvirt,
                request.Generation,
                request.NetworkName,
                network.Subnet.ToString(),
                machines,
                timeProvider.GetUtcNow());
        }
        catch (Exception importFailure)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var provisionalMachines = plans is null
                ? []
                : plans.Select((plan, index) => new OvaVirtualMachineReceipt(
                        plan.VmId,
                        DomainName(request.OperationId, request.Generation, index),
                        string.Empty))
                    .ToArray();
            try
            {
                await CleanupPartialImportAsync(
                    request.OperationId,
                    request.Generation,
                    request.NetworkName,
                    provisionalMachines,
                    cleanup.Token);
            }
            catch (Exception cleanupFailure)
            {
                throw new InvalidOperationException(
                    "OVA import failed and its partial Libvirt resources could not be removed.",
                    new AggregateException(importFailure, cleanupFailure));
            }
            throw;
        }
    }

    public async Task DestroyAsync(
        OvaRuntimeReceipt receipt,
        CancellationToken cancellationToken)
    {
        if (receipt.Provider != RuntimeProvider.Libvirt)
            throw new InvalidOperationException("OVA receipt is not owned by Libvirt.");
        var expectedNetwork = NetworkName(receipt.OperationId, receipt.Generation);
        if (!string.Equals(receipt.NetworkId, expectedNetwork, StringComparison.Ordinal)
            || receipt.VirtualMachines.Select((machine, index) =>
                    string.Equals(
                        machine.ResourceId,
                        DomainName(receipt.OperationId, receipt.Generation, index),
                        StringComparison.Ordinal))
                .Any(matches => !matches))
            throw new InvalidOperationException(
                "OVA receipt does not match its stable Runtime resource identity.");
        await DestroyDomainsAsync(receipt.VirtualMachines, cancellationToken);
        await networks.DestroyAsync(receipt.NetworkId, cancellationToken);
        DeleteDirectoryIfPresent(RuntimeDirectory(receipt.OperationId, receipt.Generation));
    }

    public async Task<IReadOnlyList<OvaManagedRuntimeResource>> ListManagedAsync(
        CancellationToken cancellationToken)
    {
        var identities = new HashSet<OvaManagedRuntimeResource>();
        foreach (var domainName in await ListDomainNamesAsync(cancellationToken))
        {
            if (TryParseResourceName(domainName, expectDomain: true, out var identity))
                identities.Add(identity);
        }
        foreach (var networkName in await networks.ListManagedNetworkNamesAsync(cancellationToken))
        {
            if (TryParseResourceName(networkName, expectDomain: false, out var identity))
                identities.Add(identity);
        }
        if (Directory.Exists(options.WorkDirectory))
        {
            foreach (var directory in Directory.EnumerateDirectories(
                         options.WorkDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory);
                if (TryParseWorkDirectoryName(name, out var identity))
                    identities.Add(identity);
            }
        }
        return identities.OrderBy(identity => identity.OperationId)
            .ThenBy(identity => identity.Generation)
            .ToArray();
    }

    public async Task DestroyByIdentityAsync(
        OvaManagedRuntimeResource identity,
        CancellationToken cancellationToken)
    {
        if (identity.OperationId == Guid.Empty || identity.Generation < 0)
            throw new InvalidOperationException("OVA managed resource identity is invalid.");
        var domains = (await ListDomainNamesAsync(cancellationToken))
            .Where(name => TryParseResourceName(name, expectDomain: true, out var parsed)
                && parsed == identity)
            .Order(StringComparer.Ordinal)
            .Select(name => new OvaVirtualMachineReceipt(string.Empty, name, string.Empty))
            .ToArray();
        await DestroyDomainsAsync(domains, cancellationToken);
        await networks.DestroyAsync(
            NetworkName(identity.OperationId, identity.Generation),
            cancellationToken);
        DeleteDirectoryIfPresent(
            RuntimeDirectory(identity.OperationId, identity.Generation));
    }

    private async Task EnsureDomainAsync(
        string domainName,
        OvfVirtualMachinePlan plan,
        int machineIndex,
        string networkName,
        string runtimeDirectory,
        CancellationToken cancellationToken)
    {
        var existing = await processes.RunAsync(
            "virsh",
            ["dominfo", domainName],
            cancellationToken);
        if (existing.ExitCode == 0)
        {
            var state = await processes.RunAsync(
                "virsh",
                ["domstate", domainName],
                cancellationToken);
            if (state.ExitCode != 0)
                throw new InvalidOperationException("Unable to inspect an existing Libvirt domain.");
            if (!state.StandardOutput.Contains("running", StringComparison.OrdinalIgnoreCase))
            {
                var start = await processes.RunAsync(
                    "virsh",
                    ["start", domainName],
                    cancellationToken);
                if (start.ExitCode != 0)
                    throw new InvalidOperationException("Unable to restart an existing Libvirt domain.");
            }
            return;
        }

        var diskDirectory = Path.Combine(runtimeDirectory, "disks");
        Directory.CreateDirectory(diskDirectory);
        var convertedDisks = new List<string>(plan.Disks.Count);
        for (var diskIndex = 0; diskIndex < plan.Disks.Count; diskIndex++)
        {
            var convertedPath = Path.Combine(
                diskDirectory,
                $"{machineIndex:D3}-{diskIndex:D3}.qcow2");
            if (File.Exists(convertedPath))
                File.Delete(convertedPath);
            var conversion = await processes.RunAsync(
                "qemu-img",
                ["convert", "-O", "qcow2", plan.Disks[diskIndex].SourcePath, convertedPath],
                cancellationToken);
            if (conversion.ExitCode != 0 || !File.Exists(convertedPath))
                throw new InvalidOperationException(
                    $"Unable to convert disk for OVF virtual machine '{plan.VmId}'.");
            convertedDisks.Add(convertedPath);
        }

        var arguments = new List<string>
        {
            "--name", domainName,
            "--import",
            "--memory", checked(plan.MemoryBytes / (1024 * 1024))
                .ToString(CultureInfo.InvariantCulture),
            "--vcpus", plan.VirtualCpuCount.ToString(CultureInfo.InvariantCulture),
            "--os-variant", "generic",
            "--network", $"network={networkName},model=virtio",
            "--channel", "unix,target.type=virtio,target.name=org.qemu.guest_agent.0",
            "--noautoconsole",
            "--wait", "0"
        };
        foreach (var disk in convertedDisks)
        {
            arguments.Add("--disk");
            arguments.Add($"path={disk},format=qcow2,bus=virtio");
        }
        var import = await processes.RunAsync(
            "virt-install",
            arguments,
            cancellationToken);
        if (import.ExitCode != 0)
            throw new InvalidOperationException(
                $"Libvirt rejected OVF virtual machine '{plan.VmId}'.");
    }

    private async Task<IPAddress> DiscoverAddressAsync(
        string domainName,
        Ipv4Cidr subnet,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        while (timeProvider.GetUtcNow() < deadline)
        {
            var state = await processes.RunAsync(
                "virsh",
                ["domstate", domainName],
                cancellationToken);
            if (state.ExitCode != 0
                || !state.StandardOutput.Contains("running", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Libvirt domain stopped before address discovery.");

            var result = await processes.RunAsync(
                "virsh",
                ["domifaddr", domainName, "--source", "agent", "--full"],
                cancellationToken);
            if (result.ExitCode == 0)
            {
                foreach (Match match in Ipv4AddressRegex().Matches(result.StandardOutput))
                {
                    if (IPAddress.TryParse(match.Groups["address"].Value, out var address)
                        && subnet.Contains(address))
                        return address;
                }
            }
            await Task.Delay(AddressPollInterval, timeProvider, cancellationToken);
        }
        throw new TimeoutException(
            $"QEMU Guest Agent did not report an address for domain '{domainName}'.");
    }

    private async Task CleanupPartialImportAsync(
        Guid operationId,
        int generation,
        string networkName,
        IReadOnlyList<OvaVirtualMachineReceipt> machines,
        CancellationToken cancellationToken)
    {
        Exception? cleanupFailure = null;
        try
        {
            await DestroyDomainsAsync(machines, cancellationToken);
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }
        try
        {
            await networks.DestroyAsync(networkName, cancellationToken);
        }
        catch (Exception exception)
        {
            cleanupFailure = cleanupFailure is null
                ? exception
                : new AggregateException(cleanupFailure, exception);
        }
        if (cleanupFailure is not null)
            throw new InvalidOperationException(
                "Partial Libvirt appliance cleanup failed.",
                cleanupFailure);
        DeleteDirectoryIfPresent(RuntimeDirectory(operationId, generation));
    }

    private async Task DestroyDomainsAsync(
        IReadOnlyList<OvaVirtualMachineReceipt> machines,
        CancellationToken cancellationToken)
    {
        var existing = await ListDomainNamesAsync(cancellationToken);
        var failed = false;
        foreach (var machine in machines.Reverse())
        {
            if (!existing.Contains(machine.ResourceId))
                continue;
            _ = await processes.RunAsync(
                "virsh",
                ["destroy", machine.ResourceId],
                cancellationToken);
            var undefine = await processes.RunAsync(
                "virsh",
                ["undefine", machine.ResourceId, "--nvram"],
                cancellationToken);
            if (undefine.ExitCode != 0)
            {
                _ = await processes.RunAsync(
                    "virsh",
                    ["undefine", machine.ResourceId],
                    cancellationToken);
            }
        }
        var remaining = await ListDomainNamesAsync(cancellationToken);
        failed = machines.Any(machine => remaining.Contains(machine.ResourceId));
        if (failed)
            throw new InvalidOperationException("One or more Libvirt domains could not be removed.");
    }

    private async Task<HashSet<string>> ListDomainNamesAsync(
        CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(
            "virsh",
            ["list", "--all", "--name"],
            cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Unable to enumerate Libvirt domains.");
        return result.StandardOutput.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private string RuntimeDirectory(Guid operationId, int generation) =>
        Path.Combine(options.WorkDirectory, $"{operationId:N}-{generation}");

    private static string DomainName(Guid operationId, int generation, int index) =>
        $"noctf-{operationId:N}-{generation}-{index:D3}";

    private static string NetworkName(Guid operationId, int generation) =>
        $"noctf-{operationId:N}-{generation}";

    private static void ValidateRequest(OvaRuntimeRequest request)
    {
        if (!request.OvaSource.IsAbsoluteUri
            || request.OvaSource.Scheme is not ("file" or "https"))
            throw new InvalidOperationException("OVA source must use file or https.");
        if (string.IsNullOrWhiteSpace(request.Sha256)
            || request.Sha256.Length != 64
            || !request.Sha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("OVA SHA-256 digest is invalid.");
        if (!string.Equals(
                request.NetworkName,
                NetworkName(request.OperationId, request.Generation),
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "OVA network name does not match its stable Runtime resource identity.");
        if (request.OperationTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("OVA operation timeout must be positive.");
    }

    private static void RecreateExtractionDirectory(string path)
    {
        DeleteDirectoryIfPresent(path);
        Directory.CreateDirectory(path);
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static bool TryParseResourceName(
        string name,
        bool expectDomain,
        out OvaManagedRuntimeResource identity)
    {
        identity = default;
        var match = ManagedResourceNameRegex().Match(name);
        if (!match.Success || match.Groups["index"].Success != expectDomain)
            return false;
        if (!Guid.TryParseExact(match.Groups["id"].Value, "N", out var operationId)
            || !int.TryParse(
                match.Groups["generation"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var generation))
            return false;
        identity = new(operationId, generation);
        return true;
    }

    private static bool TryParseWorkDirectoryName(
        string name,
        out OvaManagedRuntimeResource identity) =>
        TryParseResourceName($"noctf-{name}", expectDomain: false, out identity);

    [GeneratedRegex(@"(?<![\d.])(?<address>(?:\d{1,3}\.){3}\d{1,3})/\d{1,2}(?![\d.])")]
    private static partial Regex Ipv4AddressRegex();

    [GeneratedRegex(
        @"\Anoctf-(?<id>[0-9a-f]{32})-(?<generation>0|[1-9][0-9]*)(?:-(?<index>[0-9]{3}))?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex ManagedResourceNameRegex();
}

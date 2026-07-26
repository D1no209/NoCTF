using System.Globalization;
using System.Net;
using System.Xml.Linq;

namespace NoCTF.Runtime.Libvirt;

internal sealed record LibvirtRoutedNetwork(
    string NetworkName,
    Ipv4Cidr Subnet);

public sealed class LibvirtRoutedNetworkManager(
    ILibvirtProcessAdapter processes,
    LibvirtRuntimeOptions options)
{
    private readonly SemaphoreSlim allocationLock = new(1, 1);

    internal async Task<LibvirtRoutedNetwork> EnsureAsync(
        Guid operationId,
        int generation,
        string networkName,
        CancellationToken cancellationToken)
    {
        ValidateNetworkName(networkName);
        await allocationLock.WaitAsync(cancellationToken);
        try
        {
            var networkNames = await ListNetworkNamesAsync(
                includeInactive: true,
                cancellationToken);
            if (networkNames.Contains(networkName))
            {
                var existingSubnet = await ReadNetworkAsync(networkName, cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Libvirt Runtime network disappeared during inspection.");
                ValidateManagedSubnet(existingSubnet);
                await EnsureActiveAsync(networkName, cancellationToken);
                return new(networkName, existingSubnet);
            }

            var occupied = await ReadOccupiedSubnetsAsync(
                networkNames,
                cancellationToken);
            var subnetCount = options.RoutedNetwork.SubnetCount(
                options.RuntimeSubnetPrefixLength);
            var startIndex = Ipv4Cidr.StableIndex(operationId, generation, subnetCount);
            Ipv4Cidr? selected = null;
            for (ulong offset = 0; offset < subnetCount; offset++)
            {
                var index = (startIndex + offset) % subnetCount;
                var candidate = options.RoutedNetwork.Subnet(
                    options.RuntimeSubnetPrefixLength,
                    index);
                if (!occupied.Contains(candidate))
                {
                    selected = candidate;
                    break;
                }
            }
            if (selected is null)
                throw new InvalidOperationException(
                    "Libvirt routed network pool has no free Runtime subnet.");

            await DefineAndStartAsync(networkName, selected.Value, cancellationToken);
            return new(networkName, selected.Value);
        }
        finally
        {
            allocationLock.Release();
        }
    }

    internal async Task DestroyAsync(
        string networkName,
        CancellationToken cancellationToken)
    {
        ValidateNetworkName(networkName);
        var networkNames = await ListNetworkNamesAsync(
            includeInactive: true,
            cancellationToken);
        if (!networkNames.Contains(networkName))
            return;

        _ = await processes.RunAsync(
            "virsh",
            ["net-destroy", networkName],
            cancellationToken);
        _ = await processes.RunAsync(
            "virsh",
            ["net-undefine", networkName],
            cancellationToken);
        networkNames = await ListNetworkNamesAsync(
            includeInactive: true,
            cancellationToken);
        if (networkNames.Contains(networkName))
            throw new InvalidOperationException("Libvirt network cleanup failed.");
    }

    internal async Task<IReadOnlyList<string>> ListManagedNetworkNamesAsync(
        CancellationToken cancellationToken)
    {
        var names = await ListNetworkNamesAsync(
            includeInactive: true,
            cancellationToken);
        return names.Where(name => name.StartsWith("noctf-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<HashSet<Ipv4Cidr>> ReadOccupiedSubnetsAsync(
        IReadOnlySet<string> networkNames,
        CancellationToken cancellationToken)
    {
        var occupied = new HashSet<Ipv4Cidr>();
        foreach (var name in networkNames)
        {
            if (!name.StartsWith("noctf-", StringComparison.Ordinal))
                continue;
            var subnet = await ReadNetworkAsync(name, cancellationToken);
            if (subnet is { } value)
                occupied.Add(value);
        }
        return occupied;
    }

    private async Task<HashSet<string>> ListNetworkNamesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> arguments = includeInactive
            ? ["net-list", "--all", "--name"]
            : ["net-list", "--name"];
        var result = await processes.RunAsync("virsh", arguments, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Unable to enumerate Libvirt networks.");
        return result.StandardOutput.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task EnsureActiveAsync(
        string networkName,
        CancellationToken cancellationToken)
    {
        var active = await ListNetworkNamesAsync(
            includeInactive: false,
            cancellationToken);
        if (active.Contains(networkName))
            return;
        var result = await processes.RunAsync(
            "virsh",
            ["net-start", networkName],
            cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Unable to start the existing Libvirt network.");
    }

    private async Task<Ipv4Cidr?> ReadNetworkAsync(
        string networkName,
        CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(
            "virsh",
            ["net-dumpxml", networkName],
            cancellationToken);
        if (result.ExitCode != 0)
            return null;
        try
        {
            var document = XDocument.Parse(result.StandardOutput, LoadOptions.None);
            var ip = document.Root?.Elements("ip").SingleOrDefault();
            var address = ip?.Attribute("address")?.Value;
            var prefix = ip?.Attribute("prefix")?.Value;
            if (address is null
                || prefix is null
                || !IPAddress.TryParse(address, out var parsedAddress)
                || !int.TryParse(
                    prefix,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedPrefix))
                throw new InvalidOperationException(
                    "Libvirt network does not expose a single IPv4 CIDR.");
            return Ipv4Cidr.FromAddress(parsedAddress, parsedPrefix);
        }
        catch (Exception exception) when (
            exception is System.Xml.XmlException or ArgumentException)
        {
            throw new InvalidOperationException(
                "Libvirt returned invalid network XML.",
                exception);
        }
    }

    private async Task DefineAndStartAsync(
        string networkName,
        Ipv4Cidr subnet,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.WorkDirectory);
        var definitionPath = Path.Combine(
            options.WorkDirectory,
            $".{networkName}.{Guid.NewGuid():N}.xml");
        var subnetSize = 1u << (32 - subnet.PrefixLength);
        var gateway = subnet.Address(1);
        var dhcpStart = subnet.Address(2);
        var dhcpEnd = subnet.Address(subnetSize - 2);
        var document = new XDocument(
            new XElement(
                "network",
                new XElement("name", networkName),
                new XElement("forward", new XAttribute("mode", "route")),
                new XElement(
                    "ip",
                    new XAttribute("address", gateway),
                    new XAttribute(
                        "prefix",
                        subnet.PrefixLength.ToString(CultureInfo.InvariantCulture)),
                    new XElement(
                        "dhcp",
                        new XElement(
                            "range",
                            new XAttribute("start", dhcpStart),
                            new XAttribute("end", dhcpEnd))))));
        try
        {
            await File.WriteAllTextAsync(
                definitionPath,
                document.ToString(SaveOptions.DisableFormatting),
                cancellationToken);
            var define = await processes.RunAsync(
                "virsh",
                ["net-define", definitionPath],
                cancellationToken);
            if (define.ExitCode != 0)
                throw new InvalidOperationException("Libvirt rejected the Runtime network.");
            var start = await processes.RunAsync(
                "virsh",
                ["net-start", networkName],
                cancellationToken);
            if (start.ExitCode == 0)
                return;
            _ = await processes.RunAsync(
                "virsh",
                ["net-undefine", networkName],
                cancellationToken);
            throw new InvalidOperationException("Libvirt could not start the Runtime network.");
        }
        finally
        {
            if (File.Exists(definitionPath))
                File.Delete(definitionPath);
        }
    }

    private void ValidateManagedSubnet(Ipv4Cidr subnet)
    {
        if (subnet.PrefixLength != options.RuntimeSubnetPrefixLength
            || !options.RoutedNetwork.Contains(subnet.Address(0)))
            throw new InvalidOperationException(
                "Existing Libvirt Runtime network is outside the configured routed pool.");
    }

    private static void ValidateNetworkName(string networkName)
    {
        if (string.IsNullOrWhiteSpace(networkName)
            || networkName.Length > 63
            || networkName.Any(character =>
                character is not (>= 'a' and <= 'z')
                && character is not (>= '0' and <= '9')
                && character != '-'))
            throw new InvalidOperationException("Libvirt Runtime network name is invalid.");
    }
}

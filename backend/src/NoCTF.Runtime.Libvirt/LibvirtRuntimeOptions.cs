using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace NoCTF.Runtime.Libvirt;

public sealed record LibvirtRuntimeOptions
{
    public LibvirtRuntimeOptions(
        string cacheDirectory,
        string workDirectory,
        string poolRoutedNetworkCidr,
        string nodeRoutedNetworkCidr,
        int runtimeSubnetPrefixLength)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
            throw new ArgumentException("Libvirt cache directory is required.", nameof(cacheDirectory));
        if (string.IsNullOrWhiteSpace(workDirectory))
            throw new ArgumentException("Libvirt work directory is required.", nameof(workDirectory));

        CacheDirectory = Path.GetFullPath(cacheDirectory);
        WorkDirectory = Path.GetFullPath(workDirectory);
        PoolRoutedNetwork = Ipv4Cidr.Parse(poolRoutedNetworkCidr);
        RoutedNetwork = Ipv4Cidr.Parse(nodeRoutedNetworkCidr);
        if (RoutedNetwork.PrefixLength < PoolRoutedNetwork.PrefixLength
            || !PoolRoutedNetwork.Contains(RoutedNetwork.Address(0)))
            throw new ArgumentException(
                "Libvirt node routed network must be contained by the Pool routed network.",
                nameof(nodeRoutedNetworkCidr));
        if (runtimeSubnetPrefixLength < RoutedNetwork.PrefixLength
            || runtimeSubnetPrefixLength > 28)
        {
            throw new ArgumentOutOfRangeException(
                nameof(runtimeSubnetPrefixLength),
                "Libvirt runtime subnet prefix must be between the routed pool prefix and 28.");
        }
        RuntimeSubnetPrefixLength = runtimeSubnetPrefixLength;
    }

    public string CacheDirectory { get; }
    public string WorkDirectory { get; }
    internal Ipv4Cidr PoolRoutedNetwork { get; }
    internal Ipv4Cidr RoutedNetwork { get; }
    public int RuntimeSubnetPrefixLength { get; }
}

internal readonly record struct Ipv4Cidr(uint Network, int PrefixLength)
{
    public static Ipv4Cidr Parse(string value)
    {
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || !int.TryParse(
                parts[1],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var prefix)
            || prefix is < 1 or > 30)
        {
            throw new ArgumentException(
                "Libvirt routed network must be an IPv4 CIDR with a /1../30 prefix.",
                nameof(value));
        }

        var addressValue = ToUInt32(address);
        var mask = PrefixMask(prefix);
        if ((addressValue & mask) != addressValue)
            throw new ArgumentException(
                "Libvirt routed network must use its canonical network address.",
                nameof(value));
        return new(addressValue, prefix);
    }

    public static Ipv4Cidr FromAddress(IPAddress address, int prefixLength)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || prefixLength is < 1 or > 30)
            throw new ArgumentException("Address and prefix must describe an IPv4 network.");
        return new(ToUInt32(address) & PrefixMask(prefixLength), prefixLength);
    }

    public Ipv4Cidr Subnet(int prefixLength, ulong index)
    {
        if (prefixLength < PrefixLength || prefixLength > 30)
            throw new ArgumentOutOfRangeException(nameof(prefixLength));
        var subnetCount = 1UL << (prefixLength - PrefixLength);
        if (index >= subnetCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        var size = 1UL << (32 - prefixLength);
        return new(checked(Network + (uint)(index * size)), prefixLength);
    }

    public ulong SubnetCount(int prefixLength) =>
        1UL << (prefixLength - PrefixLength);

    public IPAddress Address(uint offset) => FromUInt32(checked(Network + offset));

    public bool Contains(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;
        return (ToUInt32(address) & PrefixMask(PrefixLength)) == Network;
    }

    public override string ToString() =>
        $"{FromUInt32(Network)}/{PrefixLength}";

    public static ulong StableIndex(Guid operationId, int generation, ulong count)
    {
        Span<byte> input = stackalloc byte[20];
        operationId.TryWriteBytes(input[..16], bigEndian: true, out _);
        BinaryPrimitives.WriteInt32BigEndian(input[16..], generation);
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);
        return BinaryPrimitives.ReadUInt64BigEndian(digest) % count;
    }

    private static uint PrefixMask(int prefix) =>
        prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);

    private static uint ToUInt32(IPAddress address)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (!address.TryWriteBytes(bytes, out var written) || written != 4)
            throw new ArgumentException("Address must be IPv4.", nameof(address));
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static IPAddress FromUInt32(uint address)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, address);
        return new IPAddress(bytes);
    }
}

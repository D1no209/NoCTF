using System.Net;

namespace NoCTF.API;

internal static class ClientIpAddress
{
    public static string? Normalize(IPAddress? address)
    {
        if (address is null)
            return null;

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    public static string? Normalize(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return address;

        return IPAddress.TryParse(address, out var parsed)
            ? Normalize(parsed)
            : address;
    }
}

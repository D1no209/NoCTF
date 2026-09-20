using System.Net;
using System.Net.Sockets;
namespace NoCTF.Infrastructure.Authentication;

public interface ISsoBackchannel
{
    Task<SsoBackchannelResponse> SendAsync(
        SsoBackchannelPolicy policy,
        HttpRequestMessage request,
        int maximumResponseBytes,
        CancellationToken cancellationToken);

    void ValidateUri(SsoBackchannelPolicy policy, Uri uri);
}

public sealed class SsoBackchannel(SsoNetworkOptions options) : ISsoBackchannel
{
    public async Task<SsoBackchannelResponse> SendAsync(
        SsoBackchannelPolicy policy,
        HttpRequestMessage request,
        int maximumResponseBytes,
        CancellationToken ct)
    {
        var uri = request.RequestUri
            ?? throw new InvalidOperationException("The SSO backchannel request URI is missing.");
        ValidateUri(policy, uri);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(policy.TimeoutSeconds),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxResponseHeadersLength = 32,
            ConnectCallback = (context, token) => ConnectAsync(policy, context.DnsEndPoint, token)
        };
        using var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(policy.TimeoutSeconds)
        };
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        var content = await ReadBoundedStringAsync(response.Content, maximumResponseBytes, ct);
        return new(response.StatusCode, content, response.Headers.Location);
    }

    public static async Task<string> ReadBoundedStringAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken ct)
    {
        if (content.Headers.ContentLength is > 0
            && content.Headers.ContentLength > maximumBytes)
            throw new InvalidOperationException("The SSO provider response exceeded the configured limit.");
        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream(Math.Min(maximumBytes, 16 * 1024));
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read == 0)
                break;
            if (output.Length + read > maximumBytes)
                throw new InvalidOperationException("The SSO provider response exceeded the configured limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return System.Text.Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    public void ValidateUri(SsoBackchannelPolicy policy, Uri uri)
    {
        if (!uri.IsAbsoluteUri
            || uri.UserInfo.Length > 0
            || !policy.AllowedHosts.Contains(uri.DnsSafeHost, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The SSO provider endpoint is outside the configured host allow-list.");
        if (uri.Scheme == Uri.UriSchemeHttps)
            return;
        if (uri.Scheme != Uri.UriSchemeHttp
            || !options.InsecureHttpHostAllowList.Contains(uri.DnsSafeHost))
            throw new InvalidOperationException("The SSO provider endpoint must use HTTPS.");
    }

    private async ValueTask<Stream> ConnectAsync(
        SsoBackchannelPolicy policy,
        DnsEndPoint endpoint,
        CancellationToken ct)
    {
        if (!policy.AllowedHosts.Contains(endpoint.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The resolved SSO endpoint host is not allowed.");
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, ct);
        var privateAllowed = options.PrivateNetworkAllowList.Contains(endpoint.Host);
        Exception? lastFailure = null;
        foreach (var address in addresses)
        {
            if (IsBlocked(address) && !privateAllowed && !AddressAllowed(address))
                continue;
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(address, endpoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                lastFailure = exception;
                if (exception is OperationCanceledException)
                    throw;
            }
        }
        throw new HttpRequestException(
            "No permitted address could be reached for the SSO provider.",
            lastFailure);
    }

    private bool AddressAllowed(IPAddress address) =>
        options.PrivateNetworkAllowList.Any(entry =>
            IPAddress.TryParse(entry, out var allowed) && allowed.Equals(address)
            || TryParseCidr(entry, out var network, out var prefix)
            && Contains(network!, prefix, address));

    internal static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
            return true;
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 16)
            return (bytes[0] & 0xFE) == 0xFC;
        return bytes[0] == 10
            || bytes[0] == 127
            || bytes[0] == 169 && bytes[1] == 254
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 100 && bytes[1] is >= 64 and <= 127
            || bytes[0] >= 224;
    }

    private static bool TryParseCidr(string value, out IPAddress? network, out int prefix)
    {
        network = null;
        prefix = 0;
        var separator = value.LastIndexOf('/');
        return separator > 0
            && IPAddress.TryParse(value[..separator], out network)
            && int.TryParse(value[(separator + 1)..], out prefix)
            && prefix >= 0
            && prefix <= network.GetAddressBytes().Length * 8;
    }

    private static bool Contains(IPAddress network, int prefix, IPAddress address)
    {
        if (network.AddressFamily != address.AddressFamily)
            return false;
        var networkBytes = network.GetAddressBytes();
        var addressBytes = address.GetAddressBytes();
        var fullBytes = prefix / 8;
        var remainingBits = prefix % 8;
        if (!networkBytes.AsSpan(0, fullBytes).SequenceEqual(addressBytes.AsSpan(0, fullBytes)))
            return false;
        if (remainingBits == 0)
            return true;
        var mask = (byte)(0xFF << (8 - remainingBits));
        return (networkBytes[fullBytes] & mask) == (addressBytes[fullBytes] & mask);
    }
}

public sealed record SsoBackchannelResponse(
    HttpStatusCode StatusCode,
    string Content,
    Uri? Location)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
}

public sealed record SsoBackchannelPolicy(
    int TimeoutSeconds,
    IReadOnlyList<string> AllowedHosts);

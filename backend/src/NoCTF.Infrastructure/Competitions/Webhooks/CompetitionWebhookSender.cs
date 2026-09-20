using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Competitions.Webhooks;

namespace NoCTF.Infrastructure.Competitions.Webhooks;

public sealed class CompetitionWebhookSender(
    CompetitionWebhookOptions options,
    TimeProvider timeProvider) : ICompetitionWebhookSender
{
    public async Task<CompetitionWebhookSendResult> SendAsync(
        CompetitionWebhookDelivery delivery,
        CancellationToken cancellationToken)
    {
        if (delivery.State != CompetitionWebhookDeliveryReadState.Ready
            || delivery.Endpoint is null
            || delivery.Body is null
            || delivery.CurrentSigningSecret is null)
            throw new InvalidOperationException("Webhook delivery is not ready.");
        ValidateUri(delivery.Endpoint);
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var messageId = delivery.EventId.ToString();
        var signatures = new List<string>
        {
            Sign(messageId, timestamp, delivery.Body, delivery.CurrentSigningSecret)
        };
        if (delivery.PreviousSigningSecret is not null)
        {
            signatures.Add(Sign(
                messageId,
                timestamp,
                delivery.Body,
                delivery.PreviousSigningSecret));
        }

        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxResponseHeadersLength = 32,
            ConnectCallback = ConnectAsync
        };
        using var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, delivery.Endpoint);
        request.Content = new ByteArrayContent(delivery.Body);
        request.Content.Headers.ContentType = new("application/cloudevents+json")
        {
            CharSet = "utf-8"
        };
        request.Headers.TryAddWithoutValidation("webhook-id", messageId);
        request.Headers.TryAddWithoutValidation(
            "webhook-timestamp",
            timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(
            "webhook-signature",
            string.Join(' ', signatures));
        request.Headers.UserAgent.ParseAdd("NoCTF-Webhook/1.0");
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException)
        {
            throw new CompetitionWebhookTransientException(
                "Webhook transport failed.",
                exception);
        }
        using (response)
        {
            var code = (int)response.StatusCode;
            if (code is >= 200 and <= 299)
                return CompetitionWebhookSendResult.Delivered;
            if (response.StatusCode == HttpStatusCode.Gone)
                return CompetitionWebhookSendResult.ReceiverGone;
            if (response.StatusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                || code >= 500)
            {
                throw new CompetitionWebhookTransientException(
                    $"Webhook receiver returned transient HTTP status {code}.");
            }
            throw new CompetitionWebhookPermanentException(
                $"Webhook receiver returned permanent HTTP status {code}.");
        }
    }

    internal static string Sign(
        string messageId,
        long timestamp,
        ReadOnlySpan<byte> body,
        string serializedSecret)
    {
        if (!serializedSecret.StartsWith("whsec_", StringComparison.Ordinal))
            throw new CryptographicException("Webhook signing secret has an invalid prefix.");
        byte[] key;
        try
        {
            key = Convert.FromBase64String(serializedSecret[6..]);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("Webhook signing secret is invalid.", exception);
        }
        if (key.Length is < 24 or > 64)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new CryptographicException("Webhook signing secret has an invalid length.");
        }
        var prefix = Encoding.UTF8.GetBytes(
            $"{messageId}.{timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        try
        {
            using var hmac = new HMACSHA256(key);
            hmac.TransformBlock(prefix, 0, prefix.Length, null, 0);
            var bodyArray = body.ToArray();
            try
            {
                hmac.TransformFinalBlock(bodyArray, 0, bodyArray.Length);
                return "v1," + Convert.ToBase64String(hmac.Hash!);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bodyArray);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.UserInfo.Length > 0)
            throw new CompetitionWebhookPermanentException("Webhook endpoint is invalid.");
        if (uri.Scheme == Uri.UriSchemeHttps)
            return;
        if (uri.Scheme != Uri.UriSchemeHttp
            || !options.InsecureHttpHostAllowList.Contains(uri.DnsSafeHost))
        {
            throw new CompetitionWebhookPermanentException(
                "Webhook endpoint must use HTTPS.");
        }
    }

    private async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);
        var privateAllowed = options.PrivateNetworkAllowList.Contains(endpoint.Host);
        Exception? lastFailure = null;
        foreach (var address in addresses)
        {
            if (IsBlocked(address)
                && !privateAllowed
                && !AddressAllowed(address))
                continue;
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(address, endpoint.Port, cancellationToken);
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
            "No permitted address could be reached for the webhook endpoint.",
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

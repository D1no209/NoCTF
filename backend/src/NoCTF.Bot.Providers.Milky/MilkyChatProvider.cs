using System.Buffers;
using System.Globalization;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Options;
using NoCTF.Bot.Providers;

namespace NoCTF.Bot.Providers.Milky;

public sealed class MilkyChatProvider(
    MilkyClient client,
    IOptions<MilkyOptions> options,
    ILogger<MilkyChatProvider> logger) : IChatProvider
{
    public const string ProviderId = "milky";
    private readonly MilkyOptions options = options.Value;

    public string Id => ProviderId;

    public ChatProviderCapabilities Capabilities { get; } = new(
        SupportsGroupMessages: true,
        SupportsGroupMemberRoles: true,
        MaximumTextLength: 3500);

    public bool TryNormalizeUserId(string value, out string normalized)
    {
        normalized = string.Empty;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0)
        {
            return false;
        }
        normalized = parsed.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    public async Task<ChatProviderIdentity> GetIdentityAsync(CancellationToken ct)
    {
        var login = await client.GetLoginInfoAsync(ct);
        var implementation = await client.GetImplementationInfoAsync(ct);
        logger.LogInformation(
            "Milky implementation {Implementation} {Version}, protocol {MilkyVersion}.",
            implementation.ImplName,
            implementation.ImplVersion,
            implementation.MilkyVersion);
        return new(
            login.Uin.ToString(CultureInfo.InvariantCulture),
            login.Nickname);
    }

    public async IAsyncEnumerable<ChatGroupMessage> ReadGroupMessagesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {options.AccessToken}");
        await socket.ConnectAsync(EventUri(options.BaseUrl), ct);
        logger.LogInformation("Connected to the Milky event stream.");
        var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var message = new MemoryStream();
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(rented, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("Milky closed the event stream.");
                if (result.MessageType != WebSocketMessageType.Text)
                    continue;
                message.Write(rented, 0, result.Count);
                if (message.Length > 64 * 1024)
                    throw new WebSocketException("Milky event exceeded the maximum accepted size.");
                if (!result.EndOfMessage) continue;
                var json = Encoding.UTF8.GetString(
                    message.GetBuffer(),
                    0,
                    checked((int)message.Length));
                message.SetLength(0);
                var parsed = MilkyEventParser.ParseGroupMessage(json);
                if (parsed is not null) yield return parsed;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
        if (!ct.IsCancellationRequested)
            throw new WebSocketException("Milky event stream ended unexpectedly.");
    }

    public async Task<ChatGroupRole> GetGroupMemberRoleAsync(
        string groupId,
        string userId,
        CancellationToken ct)
    {
        var member = await client.GetGroupMemberAsync(
            ParseId(groupId),
            ParseId(userId),
            ct);
        return member.Role switch
        {
            MilkyGroupRole.Admin => ChatGroupRole.Admin,
            MilkyGroupRole.Owner => ChatGroupRole.Owner,
            _ => ChatGroupRole.Member
        };
    }

    public async Task<ChatDeliveryReceipt> SendGroupTextAsync(
        string groupId,
        string text,
        CancellationToken ct)
    {
        var result = await client.SendGroupMessageAsync(ParseId(groupId), text, ct);
        return new(result.MessageSeq.ToString(CultureInfo.InvariantCulture));
    }

    private static long ParseId(string value) =>
        long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

    private static Uri EventUri(Uri baseUrl)
    {
        var builder = new UriBuilder(new Uri(baseUrl, "event"))
        {
            Scheme = baseUrl.Scheme == Uri.UriSchemeHttps ? "wss" : "ws"
        };
        return builder.Uri;
    }
}

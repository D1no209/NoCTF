using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.WebSockets;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.IdentityModel.Tokens;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed class LiveKitMediaGateway(IHttpClientFactory clients, LiveKitMediaOptions options, TimeProvider clock) : ILiveSoloMediaGateway
{
    public const string ClientName = "live-solo-livekit";
    private readonly SemaphoreSlim policyGate = new(1, 1);
    private DateTimeOffset policyVerifiedAt;

    private void RequireConfigured()
    {
        if (!options.Enabled || options.ApiUrl is null || options.ClientUrl is null || string.IsNullOrWhiteSpace(options.ApiKey)
            || Encoding.UTF8.GetByteCount(options.ApiSecret) < 32)
            throw new InvalidOperationException("LiveSolo media is not configured.");
    }
    private string Token(string identity, LiveKitVideoGrant grant, DateTimeOffset expires)
    {
        RequireConfigured(); var now = clock.GetUtcNow();
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, identity),
            new Claim("video", JsonSerializer.Serialize(grant, LiveKitJsonContext.Default.LiveKitVideoGrant), JsonClaimValueTypes.Json) };
        var payload = new JwtPayload(options.ApiKey, null, claims, now.UtcDateTime.AddSeconds(-5), expires.UtcDateTime, now.UtcDateTime);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(new JwtHeader(new SigningCredentials(new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(options.ApiSecret)), SecurityAlgorithms.HmacSha256)), payload));
    }
    private async Task<HttpResponseMessage> SendAsync<T>(LiveKitRoomOperation operation, T request, JsonTypeInfo<T> type,
        LiveKitVideoGrant grant, CancellationToken ct)
    {
        RequireConfigured();
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(options.ApiUrl!, "/twirp/livekit.RoomService/" + operation));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("noctf-service", grant, clock.GetUtcNow().AddMinutes(1)));
        message.Content = JsonContent.Create(request, type);
        var response = await clients.CreateClient(ClientName).SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        { response.Dispose(); throw new HttpRequestException("The media service rejected the operation."); }
        return response;
    }
    public async Task<LiveSoloMediaReadiness> CheckAsync(CancellationToken ct)
    {
        if (!options.Enabled) return new(false, false, false);
        try
        {
            using var rooms = await SendAsync(LiveKitRoomOperation.ListRooms, new LiveKitListRooms([]), LiveKitJsonContext.Default.LiveKitListRooms,
                new("", RoomList: true), ct);
            if (!rooms.IsSuccessStatusCode) return new(true, false, false);
            if (!await VerifyRoomPolicyAsync(ct)) return new(true, false, false);
            var egress = false;
            if (options.EgressHealthUrl is not null)
            {
                using var health = await clients.CreateClient(ClientName).GetAsync(options.EgressHealthUrl, ct);
                egress = health.IsSuccessStatusCode;
            }
            return new(true, true, egress);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested && exception is HttpRequestException or TaskCanceledException)
        { return new(true, false, false); }
    }
    private async Task<bool> VerifyRoomPolicyAsync(CancellationToken ct)
    {
        if (clock.GetUtcNow() - policyVerifiedAt < TimeSpan.FromSeconds(30)) return true;
        await policyGate.WaitAsync(ct);
        var room = Guid.NewGuid().ToString("N");
        try
        {
            if (clock.GetUtcNow() - policyVerifiedAt < TimeSpan.FromSeconds(30)) return true;
            await CreateRoomAsync(room, ct);
            var token = Token(Guid.NewGuid().ToString("N"), new(room, RoomJoin: true), clock.GetUtcNow().AddMinutes(1));
            var uri = new UriBuilder(options.ApiUrl!) { Scheme = options.ApiUrl!.Scheme == "https" ? "wss" : "ws",
                Path = "/rtc", Query = "protocol=16&auto_subscribe=0&sdk=js&version=2.0.0" }.Uri;
            async Task<bool> Connect()
            {
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
                try { await socket.ConnectAsync(uri, timeout.Token); socket.Abort(); return true; }
                catch (WebSocketException) { return false; }
            }
            if (!await Connect()) return false;
            await StopRoomAsync(room, ct);
            // Self-hosted tokens are not revoked by kicking. Deleted rooms must not auto-create on old tokens.
            if (await Connect()) return false;
            policyVerifiedAt = clock.GetUtcNow(); return true;
        }
        finally
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await StopRoomAsync(room, cleanup.Token);
            }
            finally { policyGate.Release(); }
        }
    }
    public async Task CreateRoomAsync(string roomIdentity, CancellationToken ct)
    {
        using var response = await SendAsync(LiveKitRoomOperation.CreateRoom,
            new LiveKitCreateRoom(roomIdentity, 86_400, 86_400, options.MaximumParticipants), LiveKitJsonContext.Default.LiveKitCreateRoom,
            new("", RoomCreate: true), ct);
        response.EnsureSuccessStatusCode();
    }
    public Task<LiveSoloMediaToken> AuthorizeAsync(LiveSoloMediaAuthorization authorization, CancellationToken ct)
    {
        RequireConfigured(); ct.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(authorization.Role) || authorization.Generation == Guid.Empty || authorization.SessionId == Guid.Empty
            || string.IsNullOrWhiteSpace(authorization.RoomIdentity) || string.IsNullOrWhiteSpace(authorization.ParticipantIdentity)
            || authorization.ExpiresAt <= clock.GetUtcNow())
            throw new InvalidOperationException("Invalid media authorization.");
        var publisher = authorization.Role == LiveSoloMediaRole.Publisher;
        var expires = authorization.ExpiresAt < clock.GetUtcNow().AddMinutes(2) ? authorization.ExpiresAt : clock.GetUtcNow().AddMinutes(2);
        return Task.FromResult(new LiveSoloMediaToken(options.ClientUrl!.AbsoluteUri, Token(authorization.ParticipantIdentity,
            new(authorization.RoomIdentity, RoomJoin: true, CanPublish: publisher,
                CanSubscribe: !publisher || authorization.MaySubscribe, CanPublishSources: publisher ? ["screen_share"] : []), expires), expires));
    }
    public async Task<LiveSoloRoomObservation> ObserveAsync(string roomIdentity, CancellationToken ct)
    {
        using var response = await SendAsync(LiveKitRoomOperation.ListParticipants, new LiveKitRoomIdentity(roomIdentity),
            LiveKitJsonContext.Default.LiveKitRoomIdentity, new(roomIdentity, RoomAdmin: true), ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return new([], false);
        var participants = await response.Content.ReadFromJsonAsync(LiveKitJsonContext.Default.LiveKitParticipants, ct)
            ?? throw new InvalidDataException("Invalid media participant response.");
        return new((participants.Participants ?? []).Select(participant =>
        {
            var track = (participant.Tracks ?? []).FirstOrDefault(x => x.Type == LiveKitTrackType.VIDEO
                && x.Source == LiveKitTrackSource.SCREEN_SHARE && !x.Muted);
            return new LiveSoloObservedScreen(participant.Identity,
                participant.State != LiveKitParticipantState.ACTIVE ? LiveSoloScreenState.Disconnected
                    : track is null ? LiveSoloScreenState.Connected : LiveSoloScreenState.Sharing,
                track?.Sid, clock.GetUtcNow());
        }).ToArray());
    }
    public async Task StopRoomAsync(string roomIdentity, CancellationToken ct)
    {
        using var response = await SendAsync(LiveKitRoomOperation.DeleteRoom, new LiveKitRoomIdentity(roomIdentity),
            LiveKitJsonContext.Default.LiveKitRoomIdentity, new("", RoomCreate: true), ct);
    }
    public async Task DisconnectAsync(string roomIdentity, string identity, CancellationToken ct)
    {
        using var response = await SendAsync(LiveKitRoomOperation.RemoveParticipant, new LiveKitParticipantIdentity(roomIdentity, identity),
            LiveKitJsonContext.Default.LiveKitParticipantIdentity, new(roomIdentity, RoomAdmin: true), ct);
    }
}

using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NSubstitute;
using NoCTF.API.LiveSolo.Realtime;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.LiveSolo.Realtime;
using NoCTF.Infrastructure.LiveSolo.Realtime;
using NoCTF.Worker.LiveSolo;
using Testcontainers;
using DotNet.Testcontainers.Builders;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloSignalRIntegrationTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    [Test, Timeout(300_000)]
    public async Task Actual_websocket_receives_nats_invalidation_and_revoked_audience_is_aborted_before_another_push(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await nats.StartAsync(ct);
            await using var connection = new NatsConnection(NatsOpts.Default with { Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}" });
            var eligible = true; var competition = Guid.NewGuid(); var match = Guid.NewGuid();
            var access = Substitute.For<ILiveSoloConnectionAccess>();
            access.EligibleAsync(Arg.Any<IReadOnlyList<LiveSoloRealtimeAccessRequest>>(), Arg.Any<CancellationToken>()).Returns(call =>
                (IReadOnlySet<string>)call.Arg<IReadOnlyList<LiveSoloRealtimeAccessRequest>>()!.Where(x => eligible && x.UserId == Actor
                    && x.CompetitionId == competition && x.MatchId == match && x.Audience == LiveSoloRealtimeAudience.Participant).Select(x => x.Key).ToHashSet());
            var mfa = Substitute.For<IMfaConnectionContextValidator>();
            mfa.ValidateAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>()).Returns(call =>
                (IReadOnlyDictionary<string, MfaFailure?>)call.Arg<IReadOnlyList<MfaContextValidationRequest>>()!.ToDictionary(x => x.Key, _ => (MfaFailure?)null));
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, Authentication>("Bearer", _ => { });
            builder.Services.AddAuthorization(); builder.Services.AddSignalR(options => options.AddFilter<MfaHubFilter>());
            builder.Services.AddSingleton(access); builder.Services.AddSingleton(mfa); builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<MfaConnectionGuard>(); builder.Services.AddSingleton<MfaHubFilter>(); builder.Services.AddSingleton<LiveSoloConnectionGuard>();
            builder.Services.AddSingleton<INatsConnection>(connection); builder.Services.AddSingleton<NatsLiveSoloRealtimeRelay>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<NatsLiveSoloRealtimeRelay>());
            await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapHub<LiveSoloHub>("/hubs/v1/live-solo");
            await app.StartAsync(ct); await app.Services.GetRequiredService<NatsLiveSoloRealtimeRelay>().Ready.WaitAsync(TimeSpan.FromSeconds(10), ct);
            using var socket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(new("ws://localhost/hubs/v1/live-solo?access_token=verified-test"), ct);
            await Send(socket, "{\"protocol\":\"json\",\"version\":1}", ct); using var handshake = await Receive(socket, ct);
            await Send(socket, JsonSerializer.Serialize(new { type = 1, invocationId = "join", target = "JoinMatch", arguments = new object[] { competition, match, 0 } }), ct);
            using var joined = await Receive(socket, ct); await Assert.That(joined.RootElement.GetProperty("type").GetInt32()).IsEqualTo(3);
            await Assert.That(joined.RootElement.TryGetProperty("error", out _)).IsFalse();
            var publisher = new LiveSoloRealtimeMessageHandler(new NatsLiveSoloRealtimePublisher(connection));
            await publisher.Handle(new(competition, match, DateTimeOffset.UtcNow), ct);
            using var pushed = await Receive(socket, ct); var root = pushed.RootElement;
            await Assert.That(root.GetProperty("target").GetString()).IsEqualTo("MatchChanged");
            var payload = root.GetProperty("arguments")[0];
            await Assert.That(payload.GetProperty("matchId").GetGuid()).IsEqualTo(match);
            await Assert.That(payload.EnumerateObject().Count()).IsEqualTo(3);
            eligible = false; await publisher.Handle(new(competition, match, DateTimeOffset.UtcNow), ct);
            await Assert.That(await app.Services.GetRequiredService<LiveSoloConnectionGuard>().EligibleAsync(competition, match, ct)).IsEmpty();
            await app.StopAsync(ct);
        });
    }
    private static Task Send(WebSocket socket, string value, CancellationToken ct)
        => socket.SendAsync(Encoding.UTF8.GetBytes(value + "\u001e"), WebSocketMessageType.Text, true, ct);
    private static async Task<JsonDocument> Receive(WebSocket socket, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var bytes = new byte[65536]; var text = new StringBuilder();
        while (true)
        {
            var received = await socket.ReceiveAsync(bytes, deadline.Token); text.Append(Encoding.UTF8.GetString(bytes, 0, received.Count));
            var value = text.ToString(); var boundary = value.IndexOf('\u001e');
            if (boundary < 0) continue;
            var parsed = JsonDocument.Parse(value[..boundary]);
            if (!parsed.RootElement.TryGetProperty("type", out var type) || type.GetInt32() != 6) return parsed;
            parsed.Dispose(); text.Clear(); text.Append(value[(boundary + 1)..]);
        }
    }
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Query["access_token"] != "verified-test") return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, Actor.ToString()), new("token_version", "0"),
                new("exp", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString())], "Bearer"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Bearer")));
        }
    }
}

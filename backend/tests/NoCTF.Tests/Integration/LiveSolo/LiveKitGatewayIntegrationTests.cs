using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Infrastructure.LiveSolo.Media;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration"), NotInParallel]
public sealed class LiveKitGatewayIntegrationTests
{
    [Test, Timeout(300_000)]
    public async Task Self_hosted_server_accepts_control_requests_and_role_tokens_do_not_grant_camera_audio_data_or_opponent_subscription(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            const string key = "noctf-test-api"; const string secret = "noctf-test-secret-with-at-least-32-bytes";
            var config = $"port: 7880\nrtc:\n  tcp_port: 7881\n  use_external_ip: false\nkeys:\n  {key}: {secret}\nroom:\n  auto_create: false\nlogging:\n  level: error\n";
            await using var server = new ContainerBuilder("livekit/livekit-server:v1.13.9@sha256:d0c04791bf63ca8dcea123571827d56ba9504bd57c8d5de60ee03b5408cc3154")
                .WithEnvironment("LIVEKIT_CONFIG", config).WithPortBinding(7880, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(7880)).Build();
            await server.StartAsync(ct);
            using var services = new ServiceCollection().AddHttpClient(LiveKitMediaGateway.ClientName).Services.BuildServiceProvider();
            var gateway = new LiveKitMediaGateway(services.GetRequiredService<IHttpClientFactory>(), new()
            {
                Enabled = true, ApiUrl = new($"http://{server.Hostname}:{server.GetMappedPublicPort(7880)}"),
                ClientUrl = new($"ws://{server.Hostname}:{server.GetMappedPublicPort(7880)}"), ApiKey = key, ApiSecret = secret
            }, TimeProvider.System);
            var readiness = await gateway.CheckAsync(ct);
            await Assert.That(readiness.Configured).IsTrue(); await Assert.That(readiness.Available).IsTrue();
            await Assert.That(readiness.EgressAvailable).IsFalse();
            var room = Guid.NewGuid().ToString("N"); await gateway.CreateRoomAsync(room, ct);
            await Assert.That((await gateway.ObserveAsync(room, ct)).Screens).IsEmpty();
            var grant = new LiveSoloMediaAuthorization(Guid.NewGuid(), Guid.NewGuid(), room, Guid.NewGuid().ToString("N"),
                LiveSoloMediaRole.Publisher, false, DateTimeOffset.UtcNow.AddMinutes(1));
            var issued = await gateway.AuthorizeAsync(grant, ct);
            var parsed = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);
            using var video = JsonDocument.Parse(parsed.Claims.Single(x => x.Type == "video").Value);
            await Assert.That(video.RootElement.GetProperty("canPublish").GetBoolean()).IsTrue();
            await Assert.That(video.RootElement.GetProperty("canSubscribe").GetBoolean()).IsFalse();
            await Assert.That(video.RootElement.GetProperty("canPublishData").GetBoolean()).IsFalse();
            await Assert.That(video.RootElement.GetProperty("canPublishSources")[0].GetString()).IsEqualTo("screen_share");
            await Assert.That(video.RootElement.GetProperty("roomAdmin").GetBoolean()).IsFalse();
            var judge = await gateway.AuthorizeAsync(grant with { Role = LiveSoloMediaRole.Judge }, ct);
            using var judgeVideo = JsonDocument.Parse(new JwtSecurityTokenHandler().ReadJwtToken(judge.Token).Claims.Single(x => x.Type == "video").Value);
            await Assert.That(judgeVideo.RootElement.GetProperty("canPublish").GetBoolean()).IsFalse();
            await Assert.That(judgeVideo.RootElement.GetProperty("canSubscribe").GetBoolean()).IsTrue();
            await gateway.StopRoomAsync(room, ct);
            await Assert.That((await gateway.ObserveAsync(room, ct)).Screens).IsEmpty();
        });
    }
}

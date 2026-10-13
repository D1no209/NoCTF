using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Infrastructure.LiveSolo.Media;
using System.Text.RegularExpressions;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration"), NotInParallel]
public sealed class LiveKitEgressIntegrationTests
{
    [Test, Timeout(300_000)]
    public async Task Real_egress_records_a_screen_track_and_writes_program_segments_on_separate_media_infrastructure(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            const string key = "noctf-egress-test"; const string secret = "noctf-egress-test-secret-over-32-bytes";
            await using var network = new NetworkBuilder().Build(); await network.CreateAsync(ct);
            await using var redis = new ContainerBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2")
                .WithNetwork(network).WithNetworkAliases("media-redis").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
            await redis.StartAsync(ct);
            var serverConfig = $"port: 7880\nrtc:\n  tcp_port: 7881\n  use_external_ip: false\nredis:\n  address: media-redis:6379\nkeys:\n  {key}: {secret}\nroom:\n  auto_create: false\nlogging:\n  level: error\n";
            await using var server = new ContainerBuilder("livekit/livekit-server:v1.13.9@sha256:d0c04791bf63ca8dcea123571827d56ba9504bd57c8d5de60ee03b5408cc3154")
                .WithNetwork(network).WithNetworkAliases("media-sfu").WithEnvironment("LIVEKIT_CONFIG", serverConfig).WithPortBinding(7880, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(7880)).Build();
            await server.StartAsync(ct);
            var egressConfig = $"api_key: {key}\napi_secret: {secret}\nws_url: ws://media-sfu:7880\ninsecure: true\nredis:\n  address: media-redis:6379\nhealth_port: 8080\nlog_level: error\n";
            await using var egress = new ContainerBuilder("livekit/egress:v1.15.0@sha256:ac244a40268ce1dd1510fbff6eb529609ec68aceff1602343f79be8ca08c355b")
                .WithNetwork(network).WithEnvironment("EGRESS_CONFIG_BODY", egressConfig).WithPortBinding(8080, true)
                .WithWorkingDirectory("/out").WithCreateParameterModifier(p =>
                { var host = p.HostConfig ??= new(); host.CapAdd = ["SYS_ADMIN"]; host.Tmpfs = new Dictionary<string, string> {
                    ["/out"] = "rw,size=256m,mode=1777",["/home/egress/tmp"]="rw,size=256m,mode=1777" }; })
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080)).Build();
            await egress.StartAsync(ct);
            using var services = new ServiceCollection().AddHttpClient(LiveKitMediaGateway.ClientName).Services
                .AddHttpClient(LiveKitMediaGateway.EgressStartClientName,client=>client.Timeout=TimeSpan.FromSeconds(30)).Services.BuildServiceProvider();
            var gateway = new LiveKitMediaGateway(services.GetRequiredService<IHttpClientFactory>(), new()
            {
                Enabled = true, ApiKey = key, ApiSecret = secret, ApiUrl = new($"http://{server.Hostname}:{server.GetMappedPublicPort(7880)}"),
                ClientUrl = new($"ws://{server.Hostname}:{server.GetMappedPublicPort(7880)}"),
                EgressHealthUrl = new($"http://{egress.Hostname}:{egress.GetMappedPublicPort(8080)}"), RequestTimeoutSeconds = 30,
            }, TimeProvider.System);
            var readiness = await gateway.CheckAsync(ct);
            await Assert.That(readiness.Available).IsTrue(); await Assert.That(readiness.EgressAvailable).IsTrue();
            var room = Guid.NewGuid().ToString("N"); await gateway.CreateRoomAsync(room, ct);
            await Assert.That(await gateway.ListAsync(room, ct)).IsEmpty();
            var token = await gateway.AuthorizeAsync(new(Guid.NewGuid(), Guid.NewGuid(), room, Guid.NewGuid().ToString("N"),
                LiveSoloMediaRole.Publisher, false, DateTimeOffset.UtcNow.AddMinutes(2)), ct);
            var fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../fixtures/live-solo-media"));
            await using var publisher = new ContainerBuilder("node:24-bookworm-slim@sha256:d6aa754f16b3197301076f047b5def2f02ea1dbbc2ca920407d46d7ec7f87b20")
                .WithNetwork(network).WithWorkingDirectory("/app")
                .WithResourceMapping(new FileInfo(Path.Combine(fixtures, "package.json")), "/app/")
                .WithResourceMapping(new FileInfo(Path.Combine(fixtures, "package-lock.json")), "/app/")
                .WithResourceMapping(new FileInfo(Path.Combine(fixtures, "publish-screen.mjs")), "/app/")
                .WithEnvironment("LIVEKIT_URL", "ws://media-sfu:7880").WithEnvironment("LIVEKIT_TOKEN", token.Token)
                .WithEnvironment("DURATION_SECONDS", "120")
                .WithCommand("sh", "-c", "npm ci --ignore-scripts --no-audit --no-fund >/dev/null && node publish-screen.mjs")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("TRACK_READY:")).Build();
            await publisher.StartAsync(ct);
            var logs = await publisher.GetLogsAsync(ct: ct);
            var trackId = Regex.Match(logs.Stdout, @"TRACK_READY:(\S+)").Groups[1].Value;
            await Assert.That(trackId).IsNotEmpty();
            await Assert.That((await gateway.ObserveAsync(room, ct)).Screens.Any(x => x.TrackId == trackId && x.State == NoCTF.Domain.LiveSolo.LiveSoloScreenState.Sharing)).IsTrue();
            var capture = new LiveSoloExportRequest(Guid.NewGuid(), room, LiveSoloExportKind.Program,LiveSoloVideoPolicy.Default);
            var job = await gateway.StartAsync(capture, ct);
            await Assert.That(job.RoomIdentity).IsEqualTo(room); await Assert.That(job.Id).IsNotEmpty();
            await Assert.That(job.State is LiveSoloExportState.Starting or LiveSoloExportState.Active).IsTrue();
            var jobs = await gateway.ListAsync(room, ct);
            await Assert.That(jobs.Any(x => x.Id == job.Id && x.OutputPrefix == "/out/live-solo/" + capture.Id.ToString("N") + "/program")).IsTrue();
            await WaitFor(gateway, room, job.Id, LiveSoloExportState.Active, ct);
            var recording = new LiveSoloExportRequest(Guid.NewGuid(), room, LiveSoloExportKind.ScreenRecording,LiveSoloVideoPolicy.Default, trackId);
            var recordJob = await gateway.StartAsync(recording, ct);
            await WaitFor(gateway, room, recordJob.Id, LiveSoloExportState.Active, ct);
            await Task.Delay(TimeSpan.FromSeconds(8), ct);
            var staging=await egress.ExecAsync(["test","-s","/home/egress/tmp/"+recordJob.Id+"/recording.mp4"],ct);
            await Assert.That(staging.ExitCode).IsEqualTo(0);
            await gateway.StopAsync(recordJob.Id, ct); await gateway.StopAsync(job.Id, ct);
            var completed = await WaitFor(gateway, room, recordJob.Id, LiveSoloExportState.Complete, ct);
            await WaitFor(gateway, room, job.Id, LiveSoloExportState.Complete, ct);
            await Assert.That(completed.Files.Any(x => x.ByteLength > 4096 && x.ObjectKey.EndsWith("/recording.mp4", StringComparison.Ordinal))).IsTrue();
            var segments = await egress.ExecAsync(["find", "live-solo/" + capture.Id.ToString("N"), "-type", "f", "-name", "*.ts"], ct);
            await Assert.That(segments.ExitCode).IsEqualTo(0);
            await Assert.That(segments.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length).IsGreaterThanOrEqualTo(2);
            var playlist = await egress.ExecAsync(["cat", "live-solo/" + capture.Id.ToString("N") + "/program.m3u8"], ct);
            var evidence = Environment.GetEnvironmentVariable("NOCTF_MEDIA_EVIDENCE_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                var resolved = Path.GetFullPath(evidence);
                var allowed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
                if (!resolved.StartsWith(allowed.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Media evidence must remain inside backend artifacts.");
                Directory.CreateDirectory(resolved);
                foreach (var path in segments.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var encoded = await egress.ExecAsync(["base64", "-w", "0", path.Trim()], ct);
                    await File.WriteAllBytesAsync(Path.Combine(resolved, Path.GetFileName(path.Trim())), Convert.FromBase64String(encoded.Stdout.Trim()), ct);
                }
                await File.WriteAllTextAsync(Path.Combine(resolved, "program.m3u8"), playlist.Stdout, ct);
            }
            var tempRoot = Path.Combine(Path.GetTempPath(), "noctf-egress-playlist-" + Guid.NewGuid().ToString("N"));
            var tempFolder = Path.Combine(tempRoot, "live-solo", capture.Id.ToString("N")); Directory.CreateDirectory(tempFolder);
            try
            {
                await File.WriteAllTextAsync(Path.Combine(tempFolder, "program.m3u8"), playlist.Stdout, ct);
                foreach (var path in segments.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    await File.WriteAllBytesAsync(Path.Combine(tempFolder, Path.GetFileName(path.Trim())), [0x47], ct);
                var parser = new LiveSoloCaptureFiles(new() { CaptureSpoolPath = tempRoot });
                var parsed = await parser.SegmentsAsync(capture.Id, ct);
                await Assert.That(parsed.Count).IsGreaterThanOrEqualTo(2);
                await Assert.That(parsed.All(x => x.Duration > TimeSpan.Zero)).IsTrue();
            }
            finally
            {
                foreach (var file in Directory.EnumerateFiles(tempFolder)) File.Delete(file);
                Directory.Delete(tempFolder); Directory.Delete(Path.Combine(tempRoot, "live-solo")); Directory.Delete(tempRoot);
            }
            await gateway.StopRoomAsync(room, ct);
            await Assert.That(await gateway.ListAsync(Guid.NewGuid().ToString("N"), ct)).IsEmpty();
        });
    }
    private static async Task<LiveSoloExportObservation> WaitFor(LiveKitMediaGateway gateway, string room, string id,
        LiveSoloExportState expected, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var job = (await gateway.ListAsync(room, ct)).Single(x => x.Id == id);
            if (job.State == expected) return job;
            if (job.State is LiveSoloExportState.Failed or LiveSoloExportState.Aborted or LiveSoloExportState.LimitReached)
                throw new InvalidOperationException("The real media export failed before the expected state.");
            await Task.Delay(1000, ct);
        }
        throw new TimeoutException("The media export did not reach the expected state.");
    }
}

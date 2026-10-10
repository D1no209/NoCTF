using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Infrastructure.LiveSolo.Media;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloVideoPolicyTests
{
    [Test]
    public async Task Video_limits_reject_invalid_dimensions_frame_rate_and_bitrate()
    {
        await Assert.That(NoCTF.Domain.LiveSolo.LiveSoloVideoLimits.Valid(1280,720,10,1_000_000)).IsTrue();
        foreach(var values in new[]{(319,180,10,1_000_000),(641,360,10,1_000_000),(640,360,31,1_000_000),(640,360,10,127_999),(640,360,10,8_000_001)})
            await Assert.That(NoCTF.Domain.LiveSolo.LiveSoloVideoLimits.Valid(values.Item1,values.Item2,values.Item3,values.Item4)).IsFalse();
    }
    [Test]
    public async Task Programme_has_one_bounded_output_and_recording_uses_passthrough_without_another_encoder()
    {
        var requests=new List<string>();var room=Guid.NewGuid().ToString("N");
        using var handler=new CaptureHandler(requests,room);
        using var services=new ServiceCollection().AddHttpClient(LiveKitMediaGateway.ClientName).ConfigurePrimaryHttpMessageHandler(()=>handler).Services
            .AddHttpClient(LiveKitMediaGateway.EgressStartClientName).ConfigurePrimaryHttpMessageHandler(()=>handler).Services.BuildServiceProvider();
        var gateway=new LiveKitMediaGateway(services.GetRequiredService<IHttpClientFactory>(),new() {
            Enabled=true,ApiKey="test",ApiSecret=new string('s',32),ApiUrl=new("http://media.invalid"),ClientUrl=new("ws://media.invalid")},TimeProvider.System);
        await gateway.StartAsync(new(Guid.NewGuid(),room,LiveSoloExportKind.Program,LiveSoloVideoPolicy.Default),CancellationToken.None);
        await gateway.StartAsync(new(Guid.NewGuid(),room,LiveSoloExportKind.ScreenRecording,LiveSoloVideoPolicy.Default,"TR_test"),CancellationToken.None);
        using var programme=JsonDocument.Parse(requests[0]);var encoding=programme.RootElement.GetProperty("advanced");
        var policy=LiveSoloVideoPolicy.Default;
        await Assert.That(encoding.GetProperty("width").GetInt32()).IsEqualTo(policy.MaximumWidth);
        await Assert.That(encoding.GetProperty("height").GetInt32()).IsEqualTo(policy.MaximumHeight);
        await Assert.That(encoding.GetProperty("framerate").GetInt32()).IsEqualTo(10);
        await Assert.That(encoding.GetProperty("video_bitrate").GetInt32()).IsEqualTo(1000);
        await Assert.That(programme.RootElement.GetProperty("outputs").GetArrayLength()).IsEqualTo(1);
        using var recording=JsonDocument.Parse(requests[1]);
        await Assert.That(recording.RootElement.TryGetProperty("advanced",out _)).IsFalse();
        await Assert.That(recording.RootElement.GetProperty("preset").GetString()).IsEqualTo("PASSTHROUGH");
        await Assert.That(recording.RootElement.GetProperty("outputs")[0].GetProperty("file").GetProperty("file_type").GetString()).IsEqualTo("DEFAULT_FILETYPE");
        await Assert.That(policy.MaximumBitrateBitsPerSecond).IsEqualTo(1_000_000);
    }
    private sealed class CaptureHandler(List<string> requests,string room):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            requests.Add(await request.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{egress_id="EG_test",room_name=room,status="EGRESS_STARTING",started_at=0,ended_at=0}),Encoding.UTF8,"application/json")};
        }
    }
}

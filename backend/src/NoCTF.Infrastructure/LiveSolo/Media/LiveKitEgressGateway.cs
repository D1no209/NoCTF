using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveKitMediaGateway : ILiveSoloEgressGateway
{
    private async Task<TResponse> EgressAsync<TRequest, TResponse>(LiveKitEgressOperation operation, TRequest request,
        JsonTypeInfo<TRequest> requestType, JsonTypeInfo<TResponse> responseType, string roomIdentity, CancellationToken ct)
    {
        RequireConfigured();
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(options.ApiUrl!, "/twirp/livekit.Egress/" + operation));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("noctf-egress", new(roomIdentity, RoomRecord: true), clock.GetUtcNow().AddMinutes(1)));
        message.Content = JsonContent.Create(request, requestType);
        using var response = await clients.CreateClient(ClientName).SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"The media export service rejected {operation}: {response.StatusCode}.", null, response.StatusCode);
        return await response.Content.ReadFromJsonAsync(responseType, ct) ?? throw new InvalidDataException("Invalid media export response.");
    }
    public async Task<LiveSoloExportObservation> StartAsync(LiveSoloExportRequest request, CancellationToken ct)
    {
        if (request.Id == Guid.Empty || !Guid.TryParseExact(request.RoomIdentity, "N", out _) || !Enum.IsDefined(request.Kind)
            || request.Kind == LiveSoloExportKind.ScreenRecording && string.IsNullOrWhiteSpace(request.VideoTrackId))
            throw new InvalidOperationException("Invalid media export request.");
        var prefix = options.EgressOutputRoot.TrimEnd('/') + "/live-solo/" + request.Id.ToString("N");
        var program = request.Kind == LiveSoloExportKind.Program;
        var input = new LiveKitStartEgress(request.RoomIdentity, program ? new("grid", true) : null,
            program ? null : new(request.VideoTrackId!), new(1280, 720, 15, 1800, 2),
            program ? [new(Segments: new(prefix + "/program", prefix + "/program.m3u8", 2, true))]
                : [new(File: new(LiveKitFileType.MP4, prefix + "/recording.mp4", true))]);
        var result = await EgressAsync(LiveKitEgressOperation.StartEgress, input, LiveKitEgressJsonContext.Default.LiveKitStartEgress,
            LiveKitEgressJsonContext.Default.LiveKitEgressInfo, request.RoomIdentity, ct);
        if (result.RoomName != request.RoomIdentity || string.IsNullOrWhiteSpace(result.EgressId)) throw new InvalidDataException("Export scope mismatch.");
        return Export(result);
    }
    public async Task<IReadOnlyList<LiveSoloExportObservation>> ListAsync(string roomIdentity, CancellationToken ct)
    {
        if (!Guid.TryParseExact(roomIdentity, "N", out _)) throw new InvalidOperationException("Invalid media room identity.");
        var result = await EgressAsync(LiveKitEgressOperation.ListEgress, new LiveKitListEgress(roomIdentity), LiveKitEgressJsonContext.Default.LiveKitListEgress,
            LiveKitEgressJsonContext.Default.LiveKitEgressList, roomIdentity, ct);
        return (result.Items ?? []).Where(x => x.RoomName == roomIdentity).Select(Export).ToArray();
    }
    public async Task StopAsync(string egressId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(egressId)) throw new InvalidOperationException("Invalid media export identity.");
        async Task<bool> Terminal()
        {
            var jobs = await EgressAsync(LiveKitEgressOperation.ListEgress, new LiveKitListEgress("", egressId), LiveKitEgressJsonContext.Default.LiveKitListEgress,
                LiveKitEgressJsonContext.Default.LiveKitEgressList, "", ct);
            var current = (jobs.Items ?? []).SingleOrDefault(x => x.EgressId == egressId);
            return current is null || current.Status is LiveKitEgressStatus.EGRESS_COMPLETE or LiveKitEgressStatus.EGRESS_FAILED
                or LiveKitEgressStatus.EGRESS_ABORTED or LiveKitEgressStatus.EGRESS_LIMIT_REACHED;
        }
        if (await Terminal()) return;
        try
        {
            _ = await EgressAsync(LiveKitEgressOperation.StopEgress, new LiveKitStopEgress(egressId), LiveKitEgressJsonContext.Default.LiveKitStopEgress,
                LiveKitEgressJsonContext.Default.LiveKitEgressInfo, "", ct);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        { if (!await Terminal()) throw; }
    }
    private static LiveSoloExportObservation Export(LiveKitEgressInfo info) => new(info.EgressId, info.RoomName, info.Status switch
    {
        LiveKitEgressStatus.EGRESS_STARTING => LiveSoloExportState.Starting,
        LiveKitEgressStatus.EGRESS_ACTIVE => LiveSoloExportState.Active,
        LiveKitEgressStatus.EGRESS_ENDING => LiveSoloExportState.Ending,
        LiveKitEgressStatus.EGRESS_COMPLETE => LiveSoloExportState.Complete,
        LiveKitEgressStatus.EGRESS_ABORTED => LiveSoloExportState.Aborted,
        LiveKitEgressStatus.EGRESS_LIMIT_REACHED => LiveSoloExportState.LimitReached,
        _ => LiveSoloExportState.Failed,
    }, NanoTime(info.StartedAt), NanoTime(info.EndedAt),
        info.Egress?.Outputs.Select(x => x.File?.Filepath ?? x.Segments?.FilenamePrefix).FirstOrDefault(x => x is not null),
        (info.FileResults ?? []).Select(x => new LiveSoloExportFile(x.Filename, x.Size)).ToArray());
    private static DateTimeOffset? NanoTime(long value) => value <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(value / 1_000_000);
}

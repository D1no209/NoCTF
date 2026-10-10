using System.Text.Json.Serialization;

namespace NoCTF.Infrastructure.LiveSolo.Media;

internal enum LiveKitEgressOperation { StartEgress, ListEgress, StopEgress }
[JsonConverter(typeof(JsonStringEnumConverter<LiveKitEgressStatus>))]
internal enum LiveKitEgressStatus { EGRESS_STARTING, EGRESS_ACTIVE, EGRESS_ENDING, EGRESS_COMPLETE, EGRESS_FAILED, EGRESS_ABORTED, EGRESS_LIMIT_REACHED }
[JsonConverter(typeof(JsonStringEnumConverter<LiveKitFileType>))]
internal enum LiveKitFileType { DEFAULT_FILETYPE = 0, MP4 = 1 }
[JsonConverter(typeof(JsonStringEnumConverter<LiveKitEncodingPreset>))]
internal enum LiveKitEncodingPreset { PASSTHROUGH }
internal sealed record LiveKitStartEgress(string RoomName, LiveKitTemplateSource? Template, LiveKitMediaSource? Media,
    LiveKitEncoding? Advanced, LiveKitOutput[] Outputs,LiveKitEncodingPreset? Preset=null);
internal sealed record LiveKitTemplateSource(string Layout, bool VideoOnly);
internal sealed record LiveKitMediaSource(string VideoTrackId);
internal sealed record LiveKitEncoding(int Width, int Height, int Framerate, int VideoBitrate, double KeyFrameInterval);
internal sealed record LiveKitOutput(LiveKitFileOutput? File = null, LiveKitSegmentsOutput? Segments = null);
internal sealed record LiveKitFileOutput(LiveKitFileType FileType, string Filepath, bool DisableManifest);
internal sealed record LiveKitSegmentsOutput(string FilenamePrefix, string PlaylistName, uint SegmentDuration, bool DisableManifest);
internal sealed record LiveKitListEgress(string RoomName, string? EgressId = null);
internal sealed record LiveKitStopEgress(string EgressId);
internal sealed record LiveKitEgressList(LiveKitEgressInfo[]? Items);
internal sealed record LiveKitEgressInfo(string EgressId, string RoomName, LiveKitEgressStatus Status, long StartedAt, long EndedAt,
    LiveKitStartEgress? Egress, LiveKitEgressFile[]? FileResults, LiveKitEgressSegments[]? SegmentResults);
internal sealed record LiveKitEgressFile(string Filename, long Size);
internal sealed record LiveKitEgressSegments(string PlaylistName, long SegmentCount);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(LiveKitStartEgress))]
[JsonSerializable(typeof(LiveKitListEgress))]
[JsonSerializable(typeof(LiveKitStopEgress))]
[JsonSerializable(typeof(LiveKitEgressList))]
[JsonSerializable(typeof(LiveKitEgressInfo))]
internal partial class LiveKitEgressJsonContext : JsonSerializerContext;

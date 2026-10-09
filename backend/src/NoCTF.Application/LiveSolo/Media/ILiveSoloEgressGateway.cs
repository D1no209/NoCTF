namespace NoCTF.Application.LiveSolo.Media;

public enum LiveSoloExportKind : short { Program, ScreenRecording }
public enum LiveSoloExportState : short { Starting, Active, Ending, Complete, Failed, Aborted, LimitReached }
public sealed record LiveSoloExportRequest(Guid Id, string RoomIdentity, LiveSoloExportKind Kind, string? VideoTrackId = null);
public sealed record LiveSoloExportFile(string ObjectKey, long ByteLength);
public sealed record LiveSoloExportObservation(string Id, string RoomIdentity, LiveSoloExportState State,
    DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, string? OutputPrefix, IReadOnlyList<LiveSoloExportFile> Files, Guid? RequestId);

public interface ILiveSoloEgressGateway
{
    Task<LiveSoloExportObservation> StartAsync(LiveSoloExportRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<LiveSoloExportObservation>> ListAsync(string roomIdentity, CancellationToken cancellationToken);
    Task StopAsync(string egressId, CancellationToken cancellationToken);
}

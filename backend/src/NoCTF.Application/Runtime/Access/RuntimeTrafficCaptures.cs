using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Access;

public sealed record RuntimeTrafficCaptureQuery(
    Guid CompetitionId,
    Guid? CompetitionChallengeId,
    Guid? TeamId,
    Guid? RuntimeInstanceId,
    bool? Truncated,
    int Offset,
    int Limit);

public sealed record RuntimeTrafficCaptureView(
    Guid RuntimeInstanceId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    RuntimeState RuntimeState,
    int SegmentCount,
    long ByteLength,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    bool Truncated,
    string? TeamName = null,
    string? ChallengeTitle = null);

public sealed record RuntimeTrafficCapturePage(
    IReadOnlyList<RuntimeTrafficCaptureView> Items,
    int Total);

public sealed record RuntimeTrafficCaptureFile(
    Stream Content,
    string FileName,
    string ContentType = "application/vnd.tcpdump.pcap");

public sealed record RuntimeTrafficCaptureArchive(
    Func<Stream, CancellationToken, Task> WriteToAsync,
    string FileName,
    string ContentType = "application/zip");

public enum RuntimeTrafficCaptureDeleteState
{
    Deleted,
    NotFound,
    RuntimeActive
}

public interface IRuntimeTrafficCaptureStore
{
    Task<RuntimeTrafficCapturePage> ListAsync(
        RuntimeTrafficCaptureQuery query,
        CancellationToken cancellationToken);

    Task<RuntimeTrafficCaptureFile?> OpenAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        CancellationToken cancellationToken);

    Task<RuntimeTrafficCaptureArchive?> ExportAsync(
        Guid competitionId,
        IReadOnlyList<Guid> runtimeInstanceIds,
        CancellationToken cancellationToken);

    Task<RuntimeTrafficCaptureDeleteState> DeleteAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        Guid actorUserId,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken);
}

public sealed class ManageRuntimeTrafficCaptures(IRuntimeTrafficCaptureStore store)
{
    public Task<RuntimeTrafficCapturePage> ListAsync(
        RuntimeTrafficCaptureQuery query,
        CancellationToken cancellationToken = default) =>
        store.ListAsync(query, cancellationToken);

    public Task<RuntimeTrafficCaptureFile?> OpenAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        CancellationToken cancellationToken = default) =>
        store.OpenAsync(competitionId, runtimeInstanceId, cancellationToken);

    public Task<RuntimeTrafficCaptureArchive?> ExportAsync(
        Guid competitionId,
        IReadOnlyList<Guid> runtimeInstanceIds,
        CancellationToken cancellationToken = default) =>
        store.ExportAsync(competitionId, runtimeInstanceIds, cancellationToken);

    public Task<RuntimeTrafficCaptureDeleteState> DeleteAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        Guid actorUserId,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken = default) =>
        store.DeleteAsync(
            competitionId,
            runtimeInstanceId,
            actorUserId,
            deletedAt,
            cancellationToken);
}

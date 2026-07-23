using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Processing;

public enum InternalResultDisposition
{
    Applied,
    Duplicate,
    Superseded,
    Conflict,
    NotFound
}

public sealed record AwdCheckResult(
    Guid RuntimeInstanceId,
    int Generation,
    long CheckerSequence,
    bool Up,
    byte[] BodySha256,
    DateTimeOffset OccurredAt);

public sealed record AwdpFixResult(
    Guid SubmissionId,
    long ProcessingVersion,
    int ExitCode,
    bool TimedOut,
    byte[] BodySha256,
    DateTimeOffset OccurredAt);

public interface IInternalResultStore
{
    Task<InternalResultDisposition> RecordAwdAsync(
        AwdCheckResult result,
        CancellationToken cancellationToken);
    Task<InternalResultDisposition> RecordAwdpAsync(
        AwdpFixResult result,
        CancellationToken cancellationToken);
}

public sealed class RecordInternalResult(IInternalResultStore store)
{
    public Task<InternalResultDisposition> AwdAsync(
        AwdCheckResult result,
        CancellationToken ct = default) =>
        store.RecordAwdAsync(result, ct);

    public Task<InternalResultDisposition> AwdpAsync(
        AwdpFixResult result,
        CancellationToken ct = default) =>
        store.RecordAwdpAsync(result, ct);
}

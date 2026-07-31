using System.Security.Cryptography;
using System.Text;
using NoCTF.Domain.Runtime;
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
    long ProcessingVersion,
    AwdServiceState State,
    DateTimeOffset OccurredAt)
{
    public static AwdCheckResult Create(
        Guid runtimeInstanceId,
        int generation,
        long checkerSequence,
        long processingVersion,
        AwdServiceState state,
        DateTimeOffset occurredAt) =>
        new(
            runtimeInstanceId,
            generation,
            checkerSequence,
            processingVersion,
            state,
            occurredAt);
}

public static class AwdServiceStateTransition
{
    public static ScoringResult? ToScoringResult(
        AwdServiceState current,
        AwdServiceState received) =>
        current == received
            ? null
            : received == AwdServiceState.Up
                ? ScoringResult.Correct
                : received == AwdServiceState.Down
                    ? ScoringResult.Wrong
                    : null;
}

public sealed record AwdpFixResult(
    Guid SubmissionId,
    Guid RuntimeInstanceId,
    int Generation,
    long ProcessingVersion,
    long RuntimeProcessingVersion,
    AwdpFixOutcome Outcome,
    byte[] BodySha256,
    DateTimeOffset OccurredAt)
{
    public static AwdpFixResult Create(
        Guid submissionId,
        Guid runtimeInstanceId,
        int generation,
        long processingVersion,
        long runtimeProcessingVersion,
        AwdpFixOutcome outcome,
        DateTimeOffset occurredAt)
    {
        var canonical = outcome switch
        {
            AwdpFixOutcome.Fixed => "Fixed",
            AwdpFixOutcome.StillVulnerable => "StillVulnerable",
            AwdpFixOutcome.RuleViolation => "RuleViolation",
            AwdpFixOutcome.ServiceUnavailable => "ServiceUnavailable",
            AwdpFixOutcome.PatchFailed => "PatchFailed",
            AwdpFixOutcome.PatchTimeout => "PatchTimeout",
            AwdpFixOutcome.PlatformFailed => "PlatformFailed",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
        };
        return new(
            submissionId,
            runtimeInstanceId,
            generation,
            processingVersion,
            runtimeProcessingVersion,
            outcome,
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)),
            occurredAt);
    }
}

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

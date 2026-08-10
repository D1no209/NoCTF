using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.Processing;

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
    public static GameplayFactResult? ToGameplayFactResult(
        AwdServiceState current,
        AwdServiceState received) =>
        current == received
            ? null
            : received == AwdServiceState.Up
                ? GameplayFactResult.ServiceUp
                : received == AwdServiceState.Down
                    ? GameplayFactResult.ServiceDown
                    : null;
}

public sealed record AwdpFixResult(
    Guid GameplayFactId,
    Guid RuntimeInstanceId,
    int Generation,
    long RuntimeProcessingVersion,
    AwdpFixOutcome Outcome,
    DateTimeOffset OccurredAt)
{
    public static AwdpFixResult Create(
        Guid gameplayFactId,
        Guid runtimeInstanceId,
        int generation,
        long runtimeProcessingVersion,
        AwdpFixOutcome outcome,
        DateTimeOffset occurredAt)
    {
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
        return new(
            gameplayFactId,
            runtimeInstanceId,
            generation,
            runtimeProcessingVersion,
            outcome,
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

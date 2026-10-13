using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Competitions.Awd;

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
    Guid GameplayFactId,
    Guid AttemptId,
    AwdProbe Connection,
    AwdProbe Functionality,
    IReadOnlyList<AwdReportedFlag> Flags,
    DateTimeOffset ObservedAt,
    DateTimeOffset ReceivedAt);

public static class AwdCheckerOutcomeMapper
{
    public static GameplayFactResult? ToGameplayFactResult(AwdHealth health) => health switch
    {
        AwdHealth.Ok => GameplayFactResult.ServiceUp,
        AwdHealth.Mumble or AwdHealth.Offline or AwdHealth.Recovering => GameplayFactResult.ServiceDown,
        AwdHealth.InternalError => null,
        _ => throw new ArgumentOutOfRangeException(nameof(health))
    };
}

public sealed record AwdpFixResult(
    Guid GameplayFactId,
    Guid RuntimeInstanceId,
    AwdpFixOutcome Outcome,
    DateTimeOffset OccurredAt)
{
    public static AwdpFixResult Create(
        Guid gameplayFactId,
        Guid runtimeInstanceId,
        AwdpFixOutcome outcome,
        DateTimeOffset occurredAt)
    {
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
        return new(
            gameplayFactId,
            runtimeInstanceId,
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
    Task<InternalResultDisposition> RecordPatchVerificationAsync(
        AwdpFixResult result,
        CancellationToken cancellationToken) =>
        RecordAwdpAsync(result, cancellationToken);
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

    public Task<InternalResultDisposition> PatchVerificationAsync(
        AwdpFixResult result,
        CancellationToken ct = default) =>
        store.RecordPatchVerificationAsync(result, ct);
}

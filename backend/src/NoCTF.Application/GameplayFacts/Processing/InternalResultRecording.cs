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

public sealed record AwdCapabilityCheckResult(Guid RuntimeInstanceId, Guid GameplayFactId, Guid AttemptId,
    IReadOnlyList<NoCTF.Application.Challenges.Capabilities.CheckProbeEvidence> Probes,
    IReadOnlyList<NoCTF.Application.Challenges.Capabilities.CheckFlagContextEvidence> FlagContexts,
    DateTimeOffset ObservedAt, DateTimeOffset ReceivedAt);

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

public sealed record FixCheckResult(Guid GameplayFactId, Guid RuntimeInstanceId,
    IReadOnlyList<NoCTF.Application.Challenges.Capabilities.CheckProbeEvidence> Probes, DateTimeOffset ObservedAt, DateTimeOffset ReceivedAt);

public interface IInternalResultStore
{
    Task<InternalResultDisposition> CompleteFixWorkAsync(NoCTF.Application.Messaging.CompleteFixExecutionWork result, CancellationToken cancellationToken);
    Task<InternalResultDisposition> FinalizeFixExecutionAsync(Guid gameplayFactId, Guid runtimeInstanceId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<InternalResultDisposition> RecordAwdCapabilityCheckAsync(AwdCapabilityCheckResult result, CancellationToken cancellationToken);
    Task<InternalResultDisposition> RecordFixCheckAsync(FixCheckResult result, CancellationToken cancellationToken);
    Task<InternalResultDisposition> RecordAwdAsync(
        AwdCheckResult result,
        CancellationToken cancellationToken);
    Task<InternalResultDisposition> RecordAwdpAsync(
        AwdpFixResult result,
        CancellationToken cancellationToken);
}

public sealed class RecordInternalResult(IInternalResultStore store)
{
    public Task<InternalResultDisposition> AwdCapabilityCheckAsync(AwdCapabilityCheckResult result, CancellationToken ct = default) =>
        store.RecordAwdCapabilityCheckAsync(result, ct);
    public Task<InternalResultDisposition> FixCheckAsync(FixCheckResult result, CancellationToken ct = default) => store.RecordFixCheckAsync(result, ct);
    public Task<InternalResultDisposition> AwdAsync(
        AwdCheckResult result,
        CancellationToken ct = default) =>
        store.RecordAwdAsync(result, ct);

    public Task<InternalResultDisposition> AwdpAsync(
        AwdpFixResult result,
        CancellationToken ct = default) =>
        store.RecordAwdpAsync(result, ct);

}

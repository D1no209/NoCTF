using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Instances;

public sealed record AdminRuntimeFilter(
    Guid CompetitionId,
    Guid? CompetitionChallengeId,
    Guid? TeamId,
    RuntimeKind? RuntimeKind,
    RuntimeProvider? Provider,
    string? RunnerPool,
    string? RunnerId,
    RuntimeState? State,
    DateTimeOffset? ExpiresBefore,
    int? HostPort);

public interface IAdminRuntimeStore
{
    Task<IReadOnlyList<RuntimeInstanceView>> ListAsync(
        AdminRuntimeFilter filter,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken);
    Task<RuntimeInstanceView?> FindAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        CancellationToken cancellationToken);
    Task<RuntimeMutationResult> MutateAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid? teamId,
        RuntimeAction action,
        TimeSpan? extension,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<RuntimeMutationResult> TerminateAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        long expectedProcessingVersion,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<RuntimeMutationResult> ForceTerminateAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        long expectedProcessingVersion,
        Guid actorUserId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class ManageAdminRuntimes(IAdminRuntimeStore store)
{
    public Task<IReadOnlyList<RuntimeInstanceView>> ListAsync(
        AdminRuntimeFilter filter,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct = default) =>
        store.ListAsync(filter, beforeCreatedAt, beforeId, limit, ct);

    public Task<RuntimeInstanceView?> GetAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        CancellationToken ct = default) =>
        store.FindAsync(competitionId, runtimeInstanceId, ct);

    public Task<RuntimeMutationResult> MutateAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid? teamId,
        RuntimeAction action,
        TimeSpan? extension,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.MutateAsync(
            competitionId, competitionChallengeId, teamId, action, extension, now, ct);

    public Task<RuntimeMutationResult> TerminateAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        long expectedProcessingVersion,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.TerminateAsync(
            competitionId,
            runtimeInstanceId,
            expectedProcessingVersion,
            actorUserId,
            now,
            ct);

    public Task<RuntimeMutationResult> ForceTerminateAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        long expectedProcessingVersion,
        Guid actorUserId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var normalizedReason = reason.Trim();
        return normalizedReason.Length is >= 8 and <= 512
            ? store.ForceTerminateAsync(
                competitionId,
                runtimeInstanceId,
                expectedProcessingVersion,
                actorUserId,
                normalizedReason,
                now,
                ct)
            : Task.FromResult(new RuntimeMutationResult(
                null,
                RuntimeMutationFailure.InvalidReason));
    }
}

public static class RuntimeForceTerminationPolicy
{
    public static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(5);

    public static DateTimeOffset? AvailableAt(RuntimeInstanceView runtime) =>
        runtime.State is RuntimeState.Provisioning or RuntimeState.Stopping
        && !string.IsNullOrWhiteSpace(runtime.RunnerId)
            ? (runtime.StateChangedAt ?? runtime.CreatedAt).Add(StuckThreshold)
            : null;

    public static bool CanForceTerminate(RuntimeInstanceView runtime, DateTimeOffset now) =>
        AvailableAt(runtime) is { } availableAt && availableAt <= now;
}

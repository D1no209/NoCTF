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
}

using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Instances;

public sealed record AdminRuntimeFilter(
    Guid CompetitionId,
    Guid? CompetitionChallengeId,
    Guid? TeamId,
    RuntimeKind? RuntimeKind,
    RuntimeProvider? Provider,
    string? RunnerId,
    RuntimeState? State,
    DateTimeOffset? ExpiresBefore,
    int? HostPort);

public sealed record RuntimeInstanceListPage(IReadOnlyList<RuntimeInstanceView> Items, int Total);
public sealed record PlatformRuntimeListPage(IReadOnlyList<PlatformRuntimeInstanceView> Items, int Total);

public enum PlatformRuntimeScope
{
    Competition,
    ChallengeTest
}

public sealed record PlatformRuntimeInstanceView(
    RuntimeInstanceView Runtime,
    PlatformRuntimeScope Scope,
    string? CompetitionTitle,
    string ChallengeTitle);

public sealed record PlatformRuntimeFilter(
    string? Search = null,
    PlatformRuntimeScope? Scope = null,
    RuntimeState? State = null,
    RuntimeKind? RuntimeKind = null);

public interface IAdminRuntimeStore
{
    Task<RuntimeInstanceListPage> ListPageAsync(
        AdminRuntimeFilter filter,
        int offset,
        int limit,
        bool desc,
        CancellationToken cancellationToken);
    Task<PlatformRuntimeListPage> ListActiveContainersPageAsync(
        PlatformRuntimeFilter filter,
        int offset,
        int limit,
        bool desc,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<RuntimeInstanceView>> ListAsync(
        AdminRuntimeFilter filter,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken,
        int offset = 0,
        bool desc = true);
    Task<IReadOnlyList<PlatformRuntimeInstanceView>> ListActiveContainersAsync(
        PlatformRuntimeFilter filter,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken,
        int offset = 0,
        bool desc = true);
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
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<RuntimeMutationResult> ForceTerminateAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        Guid actorUserId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<RuntimeInstanceView?> FindPlatformAsync(
        Guid runtimeInstanceId,
        CancellationToken cancellationToken);
    Task<RuntimeMutationResult> TerminatePlatformAsync(
        Guid runtimeInstanceId,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<RuntimeMutationResult> ForceTerminatePlatformAsync(
        Guid runtimeInstanceId,
        Guid actorUserId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class ManageAdminRuntimes(IAdminRuntimeStore store, NoCTF.Application.Runtime.Capacity.IRunnerCapacityGate? capacity = null)
{
    public Task<RuntimeInstanceListPage> ListPageAsync(
        AdminRuntimeFilter filter,
        int offset,
        int limit,
        bool desc,
        CancellationToken ct = default) =>
        store.ListPageAsync(filter, offset, limit, desc, ct);

    public async Task<PlatformRuntimeListPage> ListActiveContainersPageAsync(
        PlatformRuntimeFilter filter,
        int offset,
        int limit,
        bool desc,
        CancellationToken ct = default)
    {
        var page = await store.ListActiveContainersPageAsync(filter, offset, limit, desc, ct);
        if (capacity is null) return page;
        var waiting = await capacity.ReadWaitingAsync(page.Items
            .Where(row => row.Runtime.State is RuntimeState.Queued or RuntimeState.Provisioning)
            .Select(row => row.Runtime.Id).ToArray(), ct);
        return page with { Items = page.Items.Select(row => row with
        {
            Runtime = row.Runtime with
            {
                WaitingReason = waiting.TryGetValue(row.Runtime.Id, out var reason) ? reason : null
            }
        }).ToArray() };
    }

    public Task<IReadOnlyList<RuntimeInstanceView>> ListAsync(
        AdminRuntimeFilter filter,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct = default) =>
        store.ListAsync(filter, beforeCreatedAt, beforeId, limit, ct);

    public async Task<IReadOnlyList<PlatformRuntimeInstanceView>> ListActiveContainersAsync(
        PlatformRuntimeFilter filter,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct = default)
    {
        var rows = await store.ListActiveContainersAsync(filter, beforeCreatedAt, beforeId, limit, ct);
        if (capacity is null) return rows;
        var waiting = await capacity.ReadWaitingAsync(rows.Where(row => row.Runtime.State is RuntimeState.Queued or RuntimeState.Provisioning)
            .Select(row => row.Runtime.Id).ToArray(), ct);
        return rows.Select(row => row with
        {
            Runtime = row.Runtime with { WaitingReason = waiting.TryGetValue(row.Runtime.Id, out var reason) ? reason : null }
        }).ToArray();
    }

    public Task<RuntimeInstanceView?> GetAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        CancellationToken ct = default) =>
        WithWaitingAsync(store.FindAsync(competitionId, runtimeInstanceId, ct), ct);

    public Task<RuntimeInstanceView?> GetPlatformAsync(
        Guid runtimeInstanceId,
        CancellationToken ct = default) =>
        WithWaitingAsync(store.FindPlatformAsync(runtimeInstanceId, ct), ct);

    private async Task<RuntimeInstanceView?> WithWaitingAsync(Task<RuntimeInstanceView?> pending, CancellationToken ct)
    {
        var view = await pending;
        if (view is null || capacity is null || view.State is not (RuntimeState.Queued or RuntimeState.Provisioning)) return view;
        var waiting = await capacity.ReadWaitingAsync([view.Id], ct);
        return view with { WaitingReason = waiting.TryGetValue(view.Id, out var reason) ? reason : null };
    }

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
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.TerminateAsync(
            competitionId,
            runtimeInstanceId,
            actorUserId,
            now,
            ct);

    public Task<RuntimeMutationResult> ForceTerminateAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
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
                actorUserId,
                normalizedReason,
                now,
                ct)
            : Task.FromResult(new RuntimeMutationResult(
                null,
                RuntimeMutationFailure.InvalidReason));
    }

    public Task<RuntimeMutationResult> TerminatePlatformAsync(
        Guid runtimeInstanceId,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.TerminatePlatformAsync(runtimeInstanceId, actorUserId, now, ct);

    public Task<RuntimeMutationResult> ForceTerminatePlatformAsync(
        Guid runtimeInstanceId,
        Guid actorUserId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var normalizedReason = reason.Trim();
        return normalizedReason.Length is >= 8 and <= 512
            ? store.ForceTerminatePlatformAsync(
                runtimeInstanceId,
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

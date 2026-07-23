using NoCTF.Application.Common;

namespace NoCTF.Application.Competitions.Management;

public interface IAdminCompetitionStore
{
    Task<IReadOnlyList<CompetitionView>> ListAsync(
        Guid actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<CompetitionView?> FindAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken cancellationToken);
    Task<bool> RestoreAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> HardDeleteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<CompetitionView?> TransferOwnerAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class ListAdminCompetitions(IAdminCompetitionStore store)
{
    public Task<IReadOnlyList<CompetitionView>> ExecuteAsync(
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct = default) =>
        store.ListAsync(actorId, isAdministrator, ct);
}

public sealed class GetAdminCompetition(IAdminCompetitionStore store)
{
    public Task<CompetitionView?> ExecuteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted = false,
        CancellationToken ct = default) =>
        store.FindAsync(competitionId, actorId, isAdministrator, includeDeleted, ct);
}

public sealed class RestoreCompetition(IAdminCompetitionStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await store.RestoreAsync(competitionId, actorId, isAdministrator, now, ct)
            ? OperationResult.Success()
            : OperationResult.Failure("competition_not_found", "Competition was not found or access was denied.");
}

public sealed class HardDeleteCompetition(IAdminCompetitionStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct = default) =>
        await store.HardDeleteAsync(competitionId, actorId, isAdministrator, ct)
            ? OperationResult.Success()
            : OperationResult.Failure(
                "competition_hard_delete_conflict",
                "Competition was not found, still has dependent facts, or access was denied.");
}

public sealed class TransferCompetitionOwner(IAdminCompetitionStore store)
{
    public async Task<OperationResult<CompetitionView>> ExecuteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (ownerId == Guid.Empty)
            return OperationResult<CompetitionView>.Failure("invalid_owner_id", "OwnerId is required.");
        var result = await store.TransferOwnerAsync(
            competitionId, actorId, isAdministrator, ownerId, now, ct);
        return result is null
            ? OperationResult<CompetitionView>.Failure(
                "competition_not_found",
                "Competition was not found or access was denied.")
            : OperationResult<CompetitionView>.Success(result);
    }
}

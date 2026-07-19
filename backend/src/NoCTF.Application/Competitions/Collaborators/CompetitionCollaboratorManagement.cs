using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Collaborators;

public sealed record CompetitionCollaboratorView(Guid UserId, CompetitionCollaboratorRole Role, DateTimeOffset AddedAt);
public sealed record AddCompetitionCollaboratorCommand(Guid CompetitionId, Guid UserId, CompetitionCollaboratorRole Role, DateTimeOffset AddedAt);

public interface ICompetitionCollaboratorStore
{
    Task<bool> CanManageAsync(Guid actorId, Guid competitionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompetitionCollaboratorView>> ListAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<string?> AddOrUpdateAsync(AddCompetitionCollaboratorCommand command, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(Guid competitionId, Guid userId, CancellationToken cancellationToken);
}

public sealed class ListCompetitionCollaborators(ICompetitionCollaboratorStore store)
{
    public Task<IReadOnlyList<CompetitionCollaboratorView>> ExecuteAsync(Guid competitionId, CancellationToken ct = default) => store.ListAsync(competitionId, ct);
}

public sealed class AddCompetitionCollaborator(ICompetitionCollaboratorStore store)
{
    public async Task<OperationResult> ExecuteAsync(AddCompetitionCollaboratorCommand command, CancellationToken ct = default)
    {
        var error = await store.AddOrUpdateAsync(command, ct);
        return error is null ? OperationResult.Success() : OperationResult.Failure(error, "Collaborator was not added.");
    }
}

public sealed class RemoveCompetitionCollaborator(ICompetitionCollaboratorStore store)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid userId, CancellationToken ct = default) =>
        await store.RemoveAsync(competitionId, userId, ct)
            ? OperationResult.Success()
            : OperationResult.Failure("collaborator_not_found", "Collaborator was not found.");
}

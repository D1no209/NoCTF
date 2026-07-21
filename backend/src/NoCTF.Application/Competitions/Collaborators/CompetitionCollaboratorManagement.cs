using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Collaborators;

public sealed record CompetitionCollaboratorView(Guid UserId, CompetitionCollaboratorRole Role, DateTimeOffset AddedAt);
public sealed record AddCompetitionCollaboratorCommand(Guid CompetitionId, Guid UserId, CompetitionCollaboratorRole Role, DateTimeOffset AddedAt);

public enum CompetitionCollaboratorFailure
{
    CompetitionNotFound,
    CompetitionFinished,
    OwnerIsNotCollaborator,
    UserNotFound,
    CollaboratorNotFound
}

public interface ICompetitionCollaboratorStore
{
    Task<bool> CanManageAsync(Guid actorId, Guid competitionId, CancellationToken cancellationToken);
    Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompetitionCollaboratorView>> ListAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<CompetitionCollaboratorFailure?> AddOrUpdateAsync(AddCompetitionCollaboratorCommand command, CancellationToken cancellationToken);
    Task<CompetitionCollaboratorFailure?> RemoveAsync(Guid competitionId, Guid userId, CancellationToken cancellationToken);
}

internal static class CompetitionCollaboratorFailureProtocol
{
    public static string Code(CompetitionCollaboratorFailure failure) => failure switch
    {
        CompetitionCollaboratorFailure.CompetitionNotFound => "competition_not_found",
        CompetitionCollaboratorFailure.CompetitionFinished => "competition_finished",
        CompetitionCollaboratorFailure.OwnerIsNotCollaborator => "owner_is_not_collaborator",
        CompetitionCollaboratorFailure.UserNotFound => "user_not_found",
        CompetitionCollaboratorFailure.CollaboratorNotFound => "collaborator_not_found",
        _ => "collaborator_rejected"
    };
}

public sealed class ListCompetitionCollaborators(ICompetitionCollaboratorStore store)
{
    public Task<IReadOnlyList<CompetitionCollaboratorView>> ExecuteAsync(Guid competitionId, CancellationToken ct = default) => store.ListAsync(competitionId, ct);
}

public sealed class AddCompetitionCollaborator(ICompetitionCollaboratorStore store)
{
    public async Task<OperationResult> ExecuteAsync(AddCompetitionCollaboratorCommand command, CancellationToken ct = default)
    {
        var status = await store.GetCompetitionStatusAsync(command.CompetitionId, ct);
        if (status is null)
            return OperationResult.Failure("competition_not_found", "Competition was not found.");
        if (status == CompetitionStatus.Finished)
            return OperationResult.Failure("competition_finished", "Finished competitions are read-only.");
        var failure = await store.AddOrUpdateAsync(command, ct);
        return failure is null ? OperationResult.Success() : OperationResult.Failure(CompetitionCollaboratorFailureProtocol.Code(failure.Value), "Collaborator was not added.");
    }
}

public sealed class RemoveCompetitionCollaborator(ICompetitionCollaboratorStore store)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid userId, CancellationToken ct = default)
    {
        var status = await store.GetCompetitionStatusAsync(competitionId, ct);
        if (status is null)
            return OperationResult.Failure("competition_not_found", "Competition was not found.");
        if (status == CompetitionStatus.Finished)
            return OperationResult.Failure("competition_finished", "Finished competitions are read-only.");
        var failure = await store.RemoveAsync(competitionId, userId, ct);
        return failure is null
            ? OperationResult.Success()
            : OperationResult.Failure(CompetitionCollaboratorFailureProtocol.Code(failure.Value), "Collaborator was not removed.");
    }
}

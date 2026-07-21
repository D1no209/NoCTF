using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Collaborators;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class RemoveCompetitionCollaboratorEndpoint(RemoveCompetitionCollaborator remove, ICompetitionCollaboratorStore store, IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult>>
{
    public override void Configure() { Delete("/admin/competitions/{competitionId}/collaborators/{userId}"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId"); var userId = Route<Guid>("userId");
        if (!await store.CanManageAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var result = await remove.ExecuteAsync(competitionId, userId, ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}

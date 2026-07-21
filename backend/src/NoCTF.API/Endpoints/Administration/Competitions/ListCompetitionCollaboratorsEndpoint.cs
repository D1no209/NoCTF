using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Collaborators;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class ListCompetitionCollaboratorsEndpoint(ListCompetitionCollaborators list, ICompetitionCollaboratorStore store, IUserContext user)
    : EndpointWithoutRequest<Results<Ok<CompetitionCollaboratorListResponse>, ForbidHttpResult>>
{
    public override void Configure() { Get("/admin/competitions/{competitionId}/collaborators"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<CompetitionCollaboratorListResponse>, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (!await store.CanManageAsync(user.UserId, id, ct)) return TypedResults.Forbid();
        var items = await list.ExecuteAsync(id, ct);
        return TypedResults.Ok(new CompetitionCollaboratorListResponse(items.Select(CompetitionCollaboratorMapper.ToResponse).ToList()));
    }
}

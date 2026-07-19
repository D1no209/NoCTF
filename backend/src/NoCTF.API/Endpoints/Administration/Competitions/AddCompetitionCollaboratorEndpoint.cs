using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Collaborators;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class AddCompetitionCollaboratorEndpoint(AddCompetitionCollaborator add, ICompetitionCollaboratorStore store, IUserContext user)
    : Endpoint<AddCompetitionCollaboratorRequest, Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Post("/admin/competitions/{competitionId}/collaborators"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(AddCompetitionCollaboratorRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        if (!await store.CanManageAsync(user.UserId, request.CompetitionId, ct)) return TypedResults.Forbid();
        var result = await add.ExecuteAsync(CompetitionCollaboratorMapper.ToCommand(request, DateTimeOffset.UtcNow), ct);
        if (result.ErrorCode is "competition_not_found" or "user_not_found") return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Collaborator was not added.", detail: result.ErrorMessage);
        return TypedResults.NoContent();
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Profiles;

namespace NoCTF.API.Endpoints.Teams;

public sealed class RemoveTeamMemberEndpoint(
    RemoveTeamProfileMember remove,
    ITeamProfileStore store,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Delete("/teams/{teamId}/members/{userId}"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var teamId = Route<Guid>("teamId");
        if (!await store.CanManageAsync(user.UserId, teamId, ct)) return TypedResults.Forbid();
        var result = await remove.ExecuteAsync(teamId, Route<Guid>("userId"), ct);
        if (result.ErrorCode is "team_not_found" or "member_not_found") return TypedResults.NotFound();
        if (result.ErrorCode == "team_forbidden") return TypedResults.Forbid();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Member was not removed.", detail: result.ErrorMessage);
        return TypedResults.NoContent();
    }
}

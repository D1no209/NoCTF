using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Endpoints.Teams;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class ListAdminTeamsEndpoint(
    ListCompetitionTeams list,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    LinkGenerator links)
    : EndpointWithoutRequest<Results<Ok<TeamListResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/teams");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListTeams"));
        Summary(summary =>
        {
            summary.Summary = "Lists all competition teams for management.";
            summary.Description = "Returns registration and moderation state to authorized competition observers.";
        });
    }

    public override async Task<Results<Ok<TeamListResponse>, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var items = await list.ExecuteAsync(competitionId, includePending: true, ct);
        return TypedResults.Ok(new TeamListResponse(
            items.Select(item => TeamMapper.ToResponse(item, links, HttpContext)).ToArray()));
    }
}

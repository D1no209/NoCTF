using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Endpoints.Teams;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class GetAdminTeamRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
}

public sealed class GetAdminTeamValidator : Validator<GetAdminTeamRequest>
{
    public GetAdminTeamValidator()
    {
        RuleFor(request => request.CompetitionId).NotEmpty();
        RuleFor(request => request.TeamId).NotEmpty();
    }
}

public sealed class GetAdminTeamEndpoint(GetTeam get, ICompetitionModerationAuthorizer authorizer,
    IUserContext user, LinkGenerator links)
    : Endpoint<GetAdminTeamRequest, Results<Ok<TeamResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/teams/{teamId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetTeam"));
        Summary(summary =>
        {
            summary.Summary = "Gets a competition team for management, including pending and internal teams.";
            summary.Description = "Loads one team by its competition-scoped identity for authorized observers without depending on list pagination or public visibility.";
        });
    }

    public override async Task<Results<Ok<TeamResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(GetAdminTeamRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, ct)) return TypedResults.Forbid();
        var team = await get.ExecuteAsync(request.CompetitionId, request.TeamId, includePending: true, includeInternal: true, ct);
        return team is null ? TypedResults.NotFound() : TypedResults.Ok(TeamMapper.ToResponse(team, links, HttpContext));
    }
}

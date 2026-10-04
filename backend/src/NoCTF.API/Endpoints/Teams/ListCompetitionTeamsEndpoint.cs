using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.Application.Teams.Registration;

namespace NoCTF.API.Endpoints.Teams;

public sealed class ListCompetitionTeamsRequest { public Guid CompetitionId { get; set; } }

public sealed class ListCompetitionTeamsEndpoint(ListCompetitionTeams list, LinkGenerator links)
    : Endpoint<ListCompetitionTeamsRequest, Ok<TeamListResponse>>
{
    public override void Configure() {
        Summary(summary =>
        {
            summary.Summary = "Lists visible competition teams using cursor pagination.";
            summary.Description = summary.Summary;
        });
 Get("/competitions/{competitionId}/teams"); AllowAnonymous(); }
    public override async Task<Ok<TeamListResponse>> ExecuteAsync(ListCompetitionTeamsRequest request, CancellationToken ct)
    {
        var items = await list.ExecuteAsync(
            Route<Guid>("competitionId"),
            includePending: false,
            includeInternal: false,
            ct);
        return TypedResults.Ok(new TeamListResponse(
            items.Select(item => TeamMapper.ToResponse(item, links, HttpContext)).ToArray()));
    }
}

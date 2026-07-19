using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Teams.Registration;

namespace NoCTF.API.Endpoints.Teams;

public sealed class ListCompetitionTeamsRequest { public Guid CompetitionId { get; set; } }

public sealed class ListCompetitionTeamsEndpoint(ListCompetitionTeams list)
    : Endpoint<ListCompetitionTeamsRequest, Ok<TeamListResponse>>
{
    public override void Configure() { Get("/competitions/{competitionId}/teams"); AllowAnonymous(); }
    public override async Task<Ok<TeamListResponse>> ExecuteAsync(ListCompetitionTeamsRequest request, CancellationToken ct)
    {
        var items = await list.ExecuteAsync(Route<Guid>("competitionId"), false, ct);
        return TypedResults.Ok(new TeamListResponse(items.Select(TeamMapper.ToResponse).ToList()));
    }
}

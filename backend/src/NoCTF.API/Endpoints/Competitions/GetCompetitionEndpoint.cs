using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class GetCompetitionRequest { public Guid CompetitionId { get; set; } }

public sealed class GetCompetitionEndpoint(GetCompetition get)
    : Endpoint<GetCompetitionRequest, Results<Ok<CompetitionResponse>, NotFound>>
{
    public override void Configure() { Get("/competitions/{competitionId}"); AllowAnonymous(); }

    public override async Task<Results<Ok<CompetitionResponse>, NotFound>> ExecuteAsync(GetCompetitionRequest request, CancellationToken ct)
    {
        var view = await get.ExecuteAsync(Route<Guid>("competitionId"), false, ct);
        return view is null ? TypedResults.NotFound() : TypedResults.Ok(CompetitionMapper.ToResponse(view));
    }
}

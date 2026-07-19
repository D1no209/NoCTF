using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Competitions;

public sealed record CompetitionListResponse(IReadOnlyList<CompetitionResponse> Items);

public sealed class ListCompetitionsEndpoint(ListCompetitions list)
    : EndpointWithoutRequest<Ok<CompetitionListResponse>>
{
    public override void Configure() { Get("/competitions"); AllowAnonymous(); }

    public override async Task<Ok<CompetitionListResponse>> ExecuteAsync(CancellationToken ct)
    {
        var items = await list.ExecuteAsync(false, ct);
        return TypedResults.Ok(new CompetitionListResponse(items.Select(CompetitionMapper.ToResponse).ToList()));
    }
}

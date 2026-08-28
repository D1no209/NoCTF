using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Competitions;

public sealed record CompetitionListResponse(IReadOnlyList<CompetitionResponse> Items);

public sealed class ListCompetitionsEndpoint(
    ListCompetitions list,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Ok<CompetitionListResponse>>
{
    public override void Configure() { Get("/competitions"); AllowAnonymous(); }

    public override async Task<Ok<CompetitionListResponse>> ExecuteAsync(CancellationToken ct)
    {
        var items = await list.ExecuteAsync(false, ct);
        var now = timeProvider.GetUtcNow();
        return TypedResults.Ok(new CompetitionListResponse(
            items.Select(item => CompetitionMapper.ToResponse(item, now)).ToList()));
    }
}

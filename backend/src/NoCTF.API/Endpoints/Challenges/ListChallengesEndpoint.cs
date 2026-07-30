using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Challenges.Management;

namespace NoCTF.API.Endpoints.Challenges;

public sealed class ListChallengesRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class ListChallengesEndpoint(ListChallenges list) : Endpoint<ListChallengesRequest, Ok<ChallengeListResponse>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Lists published challenges for a competition.");
    }

    public override async Task<Ok<ChallengeListResponse>> ExecuteAsync(
        ListChallengesRequest request,
        CancellationToken ct)
    {
        var items = await list.ExecuteAsync(
            Route<Guid>("competitionId"),
            includeUnpublished: false,
            includeDeleted: false,
            ct);
        return TypedResults.Ok(ChallengeMapper.ToListResponse(items));
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Challenges.Management;

namespace NoCTF.API.Endpoints.Challenges;

public sealed class GetChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
}

public sealed class GetChallengeEndpoint(GetChallenge get) : Endpoint<GetChallengeRequest, Results<Ok<ChallengeResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{challengeId}");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Gets a published challenge.");
    }

    public override async Task<Results<Ok<ChallengeResponse>, NotFound>> ExecuteAsync(
        GetChallengeRequest request,
        CancellationToken ct)
    {
        var item = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("challengeId"),
            includeUnpublished: false,
            ct);

        return item is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeMapper.ToResponse(item));
    }
}

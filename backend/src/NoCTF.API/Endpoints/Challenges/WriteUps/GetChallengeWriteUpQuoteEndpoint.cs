using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class GetChallengeWriteUpQuoteRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid VersionId { get; set; }
}
public sealed class GetChallengeWriteUpQuoteEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<GetChallengeWriteUpQuoteRequest, Results<Ok<ChallengeWriteUpQuote>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/writeups/versions/{versionId}/quote"); AuthSchemes("Bearer");
        Description(x => x.WithName("GetChallengeWriteUpQuote"));
        Summary(x => x.Summary = "Computes a current authoritative benefit estimate without unlocking or returning content.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpQuote>, NotFound>> ExecuteAsync(GetChallengeWriteUpQuoteRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var quote = await writeUps.QuoteAsync(request.CompetitionId, request.CompetitionChallengeId, request.VersionId, user.UserId, clock.GetUtcNow(), ct);
        return quote is null ? TypedResults.NotFound() : TypedResults.Ok(quote);
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class ListMyChallengeWriteUpsRequest { public Guid CompetitionId { get; set; } }
public sealed class ListMyChallengeWriteUpsEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<ListMyChallengeWriteUpsRequest, Results<Ok<IReadOnlyList<ChallengeWriteUpResponse>>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/teams/me/challenge-writeups"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListMyChallengeWriteUps"));
        Summary(x => x.Summary = "Lists only the current eligible team's private submission metadata in one batch.");
    }
    public override async Task<Results<Ok<IReadOnlyList<ChallengeWriteUpResponse>>, NotFound>> ExecuteAsync(ListMyChallengeWriteUpsRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var items = await writeUps.ListMineAsync(request.CompetitionId, user.UserId, clock.GetUtcNow(), ct);
        return items is null ? TypedResults.NotFound() : TypedResults.Ok((IReadOnlyList<ChallengeWriteUpResponse>)items.Select(ChallengeWriteUpProtocol.Map).ToArray());
    }
}

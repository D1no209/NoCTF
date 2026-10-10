using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloHintsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionId { get; set; }
}
public sealed record LiveSoloHintResponse(Guid Id, string Content, DateTimeOffset PublishedAt);
public sealed record LiveSoloHintsResponse(IReadOnlyList<LiveSoloHintResponse> Items);
public sealed class ListLiveSoloHintsEndpoint(ILiveSoloHintReader hints, IUserContext user, TimeProvider clock)
    : Endpoint<ListLiveSoloHintsRequest, Results<Ok<LiveSoloHintsResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/hints"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListLiveSoloHints"));
        Summary(x => x.Summary = "Reads published free hints for a currently opened RoundQuestion without producing scoring or acquisition evidence.");
    }
    public override async Task<Results<Ok<LiveSoloHintsResponse>, NotFound>> ExecuteAsync(ListLiveSoloHintsRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await hints.ReadAsync(new(req.CompetitionId, req.MatchId, req.RoundId, req.QuestionId, user.UserId, clock.GetUtcNow()), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloHintsResponse(result.Select(x => new LiveSoloHintResponse(x.Id,x.Content,x.PublishedAt)).ToArray()));
    }
}

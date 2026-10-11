using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloReviewQuestionsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public bool Staff { get; set; }
}
public sealed record LiveSoloReviewQuestionResponse(Guid Id, Guid RoundId, Guid CompetitionChallengeId, int RoundNumber,
    int Replay, int Position, string? Title, DateTimeOffset? OpenedAt, bool ContentRecorded);
public sealed record LiveSoloReviewQuestionsResponse(IReadOnlyList<LiveSoloReviewQuestionResponse> Items);
public sealed class ListLiveSoloReviewQuestionsEndpoint(ReadLiveSoloReview review, IUserContext user, TimeProvider clock)
    : Endpoint<ListLiveSoloReviewQuestionsRequest, Results<Ok<LiveSoloReviewQuestionsResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/review/questions"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListLiveSoloReviewQuestions"));
    }
    public override async Task<Results<Ok<LiveSoloReviewQuestionsResponse>, NotFound>> ExecuteAsync(ListLiveSoloReviewQuestionsRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await review.ListAsync(new(req.CompetitionId, req.MatchId, user.UserId, req.Staff, clock.GetUtcNow()), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloReviewQuestionsResponse(result.Select(x =>
            new LiveSoloReviewQuestionResponse(x.Id, x.RoundId, x.CompetitionChallengeId, x.RoundNumber, x.Replay, x.Position, x.Title, x.OpenedAt, x.ContentRecorded)).ToArray()));
    }
}

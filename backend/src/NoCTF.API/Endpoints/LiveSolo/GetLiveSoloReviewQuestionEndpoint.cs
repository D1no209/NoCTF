using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloReviewQuestionRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid QuestionId { get; set; }
    public bool Staff { get; set; }
}
public sealed record LiveSoloReviewContentResponse(LiveSoloQuestionResponse Question, IReadOnlyList<LiveSoloHintResponse> Hints,
    IReadOnlyList<LiveSoloAttachmentResponse> Attachments, bool Historical);
public sealed class GetLiveSoloReviewQuestionEndpoint(ReadLiveSoloReview review, IUserContext user, TimeProvider clock)
    : Endpoint<GetLiveSoloReviewQuestionRequest, Results<Ok<LiveSoloReviewContentResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/review/questions/{questionId}"); AuthSchemes("Bearer");
        Description(x => x.WithName("GetLiveSoloReviewQuestion"));
    }
    public override async Task<Results<Ok<LiveSoloReviewContentResponse>, NotFound>> ExecuteAsync(GetLiveSoloReviewQuestionRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await review.ReadAsync(new(req.CompetitionId, req.MatchId, user.UserId, req.Staff, clock.GetUtcNow()), req.QuestionId, ct);
        if (result is null) return TypedResults.NotFound();
        var q = result.Question;
        return TypedResults.Ok(new LiveSoloReviewContentResponse(new(q.Id, q.CompetitionChallengeId, q.Position, q.Title, q.Description, q.Direction,
            q.Tags, q.OpenedAt, null), result.Hints.Select(x => new LiveSoloHintResponse(x.Id, x.Content, x.PublishedAt)).ToArray(),
            result.Attachments.Select(x => new LiveSoloAttachmentResponse(x.Id, x.FileName, x.ContentType, x.ByteLength)).ToArray(), result.Historical));
    }
}

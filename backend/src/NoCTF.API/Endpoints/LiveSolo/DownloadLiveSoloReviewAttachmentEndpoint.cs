using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class DownloadLiveSoloReviewAttachmentRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid AttachmentId { get; set; }
    public bool Staff { get; set; }
}
public sealed class DownloadLiveSoloReviewAttachmentEndpoint(ReadLiveSoloReview review, IUserContext user, TimeProvider clock)
    : Endpoint<DownloadLiveSoloReviewAttachmentRequest, Results<FileStreamHttpResult, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/review/questions/{questionId}/attachments/{attachmentId}"); AuthSchemes("Bearer");
        Options(x => x.WithMetadata(new AttachmentBrowserDownloadMetadata()));
        Description(x => x.WithName("DownloadLiveSoloReviewAttachment"));
    }
    public override async Task<Results<FileStreamHttpResult, NotFound, ProblemHttpResult>> ExecuteAsync(DownloadLiveSoloReviewAttachmentRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        try
        {
            var result = await review.OpenAsync(new(req.CompetitionId, req.MatchId, user.UserId, req.Staff, clock.GetUtcNow()), req.QuestionId, req.AttachmentId, ct);
            return result is { } file ? TypedResults.Stream(file.Content, file.Metadata.ContentType, file.Metadata.FileName, enableRangeProcessing: file.Content.CanSeek) : TypedResults.NotFound();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TimeoutException)
        { return LiveSoloProtocol.Unavailable(LiveSoloFailure.DependencyUnavailable); }
    }
}

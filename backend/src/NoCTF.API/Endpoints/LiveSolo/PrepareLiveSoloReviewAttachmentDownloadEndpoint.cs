using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class PrepareLiveSoloReviewAttachmentDownloadRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid AttachmentId { get; set; }
    public bool Staff { get; set; }
}
public sealed class PrepareLiveSoloReviewAttachmentDownloadEndpoint(ReadLiveSoloReview review, AttachmentBrowserDownload browser, IUserContext user, TimeProvider clock)
    : Endpoint<PrepareLiveSoloReviewAttachmentDownloadRequest, Results<Ok<AttachmentBrowserDownloadResponse>, NotFound>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/review/questions/{questionId}/attachments/{attachmentId}/browser-download"); AuthSchemes("Bearer");
        Description(x => x.WithName("PrepareLiveSoloReviewAttachmentDownload"));
    }
    public override async Task<Results<Ok<AttachmentBrowserDownloadResponse>, NotFound>> ExecuteAsync(PrepareLiveSoloReviewAttachmentDownloadRequest req, CancellationToken ct)
    {
        if (await review.FileAsync(new(req.CompetitionId, req.MatchId, user.UserId, req.Staff, clock.GetUtcNow()), req.QuestionId, req.AttachmentId, ct) is null)
            return TypedResults.NotFound();
        var path = $"/api/v1/competitions/{req.CompetitionId}/live-solo/matches/{req.MatchId}/review/questions/{req.QuestionId}/attachments/{req.AttachmentId}";
        browser.Write(HttpContext, path);
        return TypedResults.Ok(new AttachmentBrowserDownloadResponse(req.Staff ? path + "?staff=true" : path));
    }
}

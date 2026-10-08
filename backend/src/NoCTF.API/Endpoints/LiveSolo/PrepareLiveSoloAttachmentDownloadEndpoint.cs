using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class PrepareLiveSoloAttachmentDownloadRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid AttachmentId { get; set; }
}
public sealed class PrepareLiveSoloAttachmentDownloadEndpoint(AccessLiveSoloAttachments attachments, AttachmentBrowserDownload browser, IUserContext user, TimeProvider clock)
    : Endpoint<PrepareLiveSoloAttachmentDownloadRequest, Results<Ok<AttachmentBrowserDownloadResponse>, NotFound>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/attachments/{attachmentId}/browser-download"); AuthSchemes("Bearer");
        Description(x => x.WithName("PrepareLiveSoloAttachmentDownload"));
        Summary(x => x.Summary = "Authorizes a native browser handoff without recording evidence or assigning random attachments.");
    }
    public override async Task<Results<Ok<AttachmentBrowserDownloadResponse>, NotFound>> ExecuteAsync(PrepareLiveSoloAttachmentDownloadRequest req, CancellationToken ct)
    {
        if (!await attachments.PrepareBrowserAsync(new(req.CompetitionId, req.MatchId, req.RoundId, req.QuestionId, user.UserId, clock.GetUtcNow()), req.AttachmentId, ct))
            return TypedResults.NotFound();
        var path = $"/api/v1/competitions/{req.CompetitionId}/live-solo/matches/{req.MatchId}/rounds/{req.RoundId}/questions/{req.QuestionId}/attachments/{req.AttachmentId}";
        browser.Write(HttpContext, path); return TypedResults.Ok(new AttachmentBrowserDownloadResponse(path));
    }
}

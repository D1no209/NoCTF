using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;

namespace NoCTF.API.Endpoints.Challenges;

public sealed class PrepareChallengeAttachmentDownloadRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid AttachmentId { get; set; }
}
public sealed class PrepareChallengeAttachmentDownloadValidator : Validator<PrepareChallengeAttachmentDownloadRequest>
{
    public PrepareChallengeAttachmentDownloadValidator()
    {
        RuleFor(value => value.CompetitionId).NotEmpty(); RuleFor(value => value.CompetitionChallengeId).NotEmpty(); RuleFor(value => value.AttachmentId).NotEmpty();
    }
}
public sealed record AttachmentBrowserDownloadResponse(string DownloadUrl);
public sealed class PrepareChallengeAttachmentDownloadEndpoint(PrepareChallengeAttachmentDownload prepare, AttachmentBrowserDownload browser, IUserContext user)
    : Endpoint<PrepareChallengeAttachmentDownloadRequest, Results<Ok<AttachmentBrowserDownloadResponse>, NotFound>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments/{attachmentId}/browser-download"); AuthSchemes("Bearer");
        Summary(value => { value.Summary = "Authorizes a native browser attachment download."; value.Description = "Sets a short-lived encrypted HttpOnly cookie restricted to this attachment GET. File bytes are streamed directly by the browser; preparation does not record download evidence."; });
    }
    public override async Task<Results<Ok<AttachmentBrowserDownloadResponse>, NotFound>> ExecuteAsync(PrepareChallengeAttachmentDownloadRequest request, CancellationToken ct)
    {
        if (!await prepare.ExecuteAsync(request.CompetitionId, request.CompetitionChallengeId, request.AttachmentId, user.UserId, ct)) return TypedResults.NotFound();
        var path = $"/api/v1/competitions/{request.CompetitionId}/challenges/{request.CompetitionChallengeId}/attachments/{request.AttachmentId}";
        browser.Write(HttpContext, path); return TypedResults.Ok(new AttachmentBrowserDownloadResponse(path));
    }
}

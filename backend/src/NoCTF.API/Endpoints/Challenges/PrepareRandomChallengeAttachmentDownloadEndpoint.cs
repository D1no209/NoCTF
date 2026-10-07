using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;

namespace NoCTF.API.Endpoints.Challenges;

public sealed class PrepareRandomChallengeAttachmentDownloadRequest { public Guid CompetitionId { get; set; } public Guid CompetitionChallengeId { get; set; } }
public sealed class PrepareRandomChallengeAttachmentDownloadValidator : Validator<PrepareRandomChallengeAttachmentDownloadRequest>
{
    public PrepareRandomChallengeAttachmentDownloadValidator() { RuleFor(value => value.CompetitionId).NotEmpty(); RuleFor(value => value.CompetitionChallengeId).NotEmpty(); }
}
public sealed class PrepareRandomChallengeAttachmentDownloadEndpoint(PrepareChallengeAttachmentDownload prepare, AttachmentBrowserDownload browser, IUserContext user)
    : Endpoint<PrepareRandomChallengeAttachmentDownloadRequest, Results<Ok<AttachmentBrowserDownloadResponse>, NotFound>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/attachment/browser-download"); AuthSchemes("Bearer");
        Summary(value => { value.Summary = "Authorizes a native browser RandomOnePerTeam attachment download."; value.Description = "Random assignment and download evidence are recorded by the authenticated file GET, never by preparing the browser handoff."; });
    }
    public override async Task<Results<Ok<AttachmentBrowserDownloadResponse>, NotFound>> ExecuteAsync(PrepareRandomChallengeAttachmentDownloadRequest request, CancellationToken ct)
    {
        if (!await prepare.ExecuteAsync(request.CompetitionId, request.CompetitionChallengeId, null, user.UserId, ct)) return TypedResults.NotFound();
        var path = $"/api/v1/competitions/{request.CompetitionId}/challenges/{request.CompetitionChallengeId}/attachment";
        browser.Write(HttpContext, path); return TypedResults.Ok(new AttachmentBrowserDownloadResponse(path));
    }
}

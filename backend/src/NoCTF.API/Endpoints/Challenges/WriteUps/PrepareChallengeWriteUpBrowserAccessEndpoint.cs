using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class PrepareChallengeWriteUpBrowserAccessRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid VersionId { get; set; }
    public bool Staff { get; set; }
}
public sealed record WriteUpBrowserAccessResponse(string PreviewUrl, string DownloadUrl);
public sealed class PrepareChallengeWriteUpBrowserAccessEndpoint(ManageChallengeWriteUps writeUps, WriteUpBrowserAccess browser,
    IUserContext user, TimeProvider clock)
    : Endpoint<PrepareChallengeWriteUpBrowserAccessRequest, Results<Ok<WriteUpBrowserAccessResponse>, NotFound>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/writeups/versions/{versionId}/browser-access"); AuthSchemes("Bearer");
        Description(x => x.WithName("PrepareChallengeWriteUpBrowserAccess"));
        Summary(x => x.Summary = "Authorizes progressive PDF preview and native download without granting other business permissions.");
    }
    public override async Task<Results<Ok<WriteUpBrowserAccessResponse>, NotFound>> ExecuteAsync(PrepareChallengeWriteUpBrowserAccessRequest request, CancellationToken ct)
    {
        var content = await writeUps.ReadContentAsync(request.CompetitionId, request.CompetitionChallengeId,
            request.VersionId, user.UserId, request.Staff, clock.GetUtcNow(), ct);
        if (content.Failure is not null || content.Format != WriteUpFormat.Pdf || content.File is null) return TypedResults.NotFound();
        var path = $"/api/v1/competitions/{request.CompetitionId}/challenges/{request.CompetitionChallengeId}/writeups/versions/{request.VersionId}/file";
        browser.Write(HttpContext, path);
        var query = request.Staff ? "?staff=true" : "?staff=false";
        return TypedResults.Ok(new WriteUpBrowserAccessResponse(path + query, path + query + "&download=true"));
    }
}

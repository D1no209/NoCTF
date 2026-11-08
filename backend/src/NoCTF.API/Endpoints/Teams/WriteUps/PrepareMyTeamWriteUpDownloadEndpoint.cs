using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.WriteUps;

namespace NoCTF.API.Endpoints.Teams.WriteUps;

public sealed record TeamWriteUpBrowserDownloadResponse(string DownloadUrl);

public sealed class PrepareMyTeamWriteUpDownloadEndpoint(
    ManageTeamWriteUps writeUps, AttachmentBrowserDownload browser, IUserContext user)
    : EndpointWithoutRequest<Results<Ok<TeamWriteUpBrowserDownloadResponse>, NotFound>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/teams/me/writeup/browser-download");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("PrepareMyTeamWriteUpDownload"));
        Summary(value => value.Summary = "Authorizes a native browser download of the current team's PDF WriteUp.");
    }

    public override async Task<Results<Ok<TeamWriteUpBrowserDownloadResponse>, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (await writeUps.FindMineAsync(competitionId, user.UserId, ct) is null)
            return TypedResults.NotFound();
        var path = $"/api/v1/competitions/{competitionId}/teams/me/writeup/content";
        browser.Write(HttpContext, path);
        return TypedResults.Ok(new TeamWriteUpBrowserDownloadResponse(path));
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.WriteUps;

namespace NoCTF.API.Endpoints.Teams.WriteUps;

public sealed class PrepareTeamWriteUpDownloadEndpoint(
    ManageTeamWriteUps writeUps, ICompetitionModerationAuthorizer authorizer,
    AttachmentBrowserDownload browser, IUserContext user)
    : EndpointWithoutRequest<Results<Ok<TeamWriteUpBrowserDownloadResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/teams/{teamId}/writeup/browser-download");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("PrepareTeamWriteUpDownload"));
        Summary(value => value.Summary = "Authorizes a native browser download of a team's PDF WriteUp for staff review.");
    }

    public override async Task<Results<Ok<TeamWriteUpBrowserDownloadResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var teamId = Route<Guid>("teamId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        if (await writeUps.FindAsync(competitionId, teamId, ct) is null)
            return TypedResults.NotFound();
        var path = $"/api/v1/competitions/{competitionId}/teams/{teamId}/writeup/content";
        browser.Write(HttpContext, path);
        return TypedResults.Ok(new TeamWriteUpBrowserDownloadResponse(path));
    }
}

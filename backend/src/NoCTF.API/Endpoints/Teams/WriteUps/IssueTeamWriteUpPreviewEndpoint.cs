using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.WriteUps;

namespace NoCTF.API.Endpoints.Teams.WriteUps;

public sealed record TeamWriteUpPreviewResponse(
    string PreviewUrl,
    DateTimeOffset ExpiresAt);

public sealed class IssueTeamWriteUpPreviewEndpoint(
    ManageTeamWriteUps writeUps,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TeamWriteUpPreviewTicketCodec tickets)
    : EndpointWithoutRequest<Results<Ok<TeamWriteUpPreviewResponse>, NotFound,
        ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/teams/{teamId}/writeup/preview");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("IssueTeamWriteUpPreview"));
        Summary(summary =>
        {
            summary.Summary = "Issues a short-lived inline preview grant for a team WriteUp.";
            summary.Description = "Authorized competition staff receive an HttpOnly, same-site grant scoped to the selected immutable PDF.";
        });
    }

    public override async Task<Results<Ok<TeamWriteUpPreviewResponse>, NotFound,
        ForbidHttpResult>> ExecuteAsync(CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var competitionId = Route<Guid>("competitionId");
        var teamId = Route<Guid>("teamId");
        if (!await authorizer.CanObserveAsync(
                user.UserId,
                competitionId,
                cancellationToken))
        {
            return TypedResults.Forbid();
        }

        var writeUp = await writeUps.FindAsync(
            competitionId,
            teamId,
            cancellationToken);
        if (writeUp is null)
            return TypedResults.NotFound();

        var grant = tickets.Issue(competitionId, teamId, writeUp.FileId);
        HttpContext.Response.Cookies.Append(
            tickets.CookieName,
            grant.Token,
            tickets.CookieOptions(competitionId, teamId));
        return TypedResults.Ok(new TeamWriteUpPreviewResponse(
            grant.PreviewUrl,
            grant.ExpiresAt));
    }
}

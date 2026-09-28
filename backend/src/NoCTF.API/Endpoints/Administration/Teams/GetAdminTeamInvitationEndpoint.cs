using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class GetAdminTeamInvitationRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
}

public sealed record AdminTeamInvitationResponse(string InvitationToken);

public sealed class GetAdminTeamInvitationEndpoint(
    GetAdminTeamInvitation get,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetAdminTeamInvitationRequest,
        Results<Ok<AdminTeamInvitationResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/teams/{teamId}/invitation-token");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetTeamInvitation"));
        Summary(summary =>
        {
            summary.Summary = "Reads a team invitation token for administration.";
            summary.Description = "Platform administrators and competition owners or managers may read the current token. The response is never cached.";
        });
    }

    public override async Task<Results<Ok<AdminTeamInvitationResponse>, NotFound,
        ForbidHttpResult>> ExecuteAsync(
        GetAdminTeamInvitationRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.TeamId = Route<Guid>("teamId");
        if (!await authorizer.CanModerateAsync(
                user.UserId,
                request.CompetitionId,
                cancellationToken))
            return TypedResults.Forbid();

        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var token = await get.ExecuteAsync(
            request.CompetitionId,
            request.TeamId,
            cancellationToken);
        return token is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new AdminTeamInvitationResponse(token));
    }
}

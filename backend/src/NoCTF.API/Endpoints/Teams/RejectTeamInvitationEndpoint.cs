using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Membership;

namespace NoCTF.API.Endpoints.Teams;

public sealed class RejectTeamInvitationEndpoint(RespondToTeamInvitation respond, IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ProblemHttpResult>>
{
    public override void Configure() { Post("/team-invitations/{invitationId}/reject"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, NotFound, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var result = await respond.ExecuteAsync(Route<Guid>("invitationId"), user.UserId, false, DateTimeOffset.UtcNow, ct);
        if (result.ErrorCode == "invitation_not_found") return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Invitation was not rejected.", detail: result.ErrorMessage);
        return TypedResults.NoContent();
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Membership;

namespace NoCTF.API.Endpoints.Teams;

public sealed class InviteTeamMemberEndpoint(InviteTeamMember invite, IUserContext user)
    : Endpoint<InviteTeamMemberRequest, Results<Created<TeamInvitationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Post("/competitions/{competitionId}/teams/{teamId}/invitations"); AuthSchemes("Bearer"); }
    public override async Task<Results<Created<TeamInvitationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(InviteTeamMemberRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId"); request.TeamId = Route<Guid>("teamId");
        var now = DateTimeOffset.UtcNow;
        var result = await invite.ExecuteAsync(TeamInvitationMapper.ToCommand(request, user.UserId, now, now.AddDays(2)), ct);
        if (result.ErrorCode is "team_not_found" or "user_not_found") return TypedResults.NotFound();
        if (result.ErrorCode == "team_forbidden") return TypedResults.Forbid();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Invitation was not created.", detail: result.ErrorMessage);
        var response = TeamInvitationMapper.ToResponse(result.Value!);
        return TypedResults.Created($"/team-invitations/{response.Id}", response);
    }
}

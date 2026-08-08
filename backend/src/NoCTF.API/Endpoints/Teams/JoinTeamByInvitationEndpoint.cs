using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Membership;

namespace NoCTF.API.Endpoints.Teams;

public sealed class JoinTeamByInvitationRequest { public Guid CompetitionId { get; set; } public string InvitationToken { get; set; } = string.Empty; }
public sealed class JoinTeamByInvitationEndpoint(JoinTeamByInvitation join, IUserContext user) : Endpoint<JoinTeamByInvitationRequest, Results<NoContent, ProblemHttpResult>>
{
    public override void Configure() { Post("/competitions/{competitionId}/teams/join"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(JoinTeamByInvitationRequest request, CancellationToken ct)
    {
        var result = await join.ExecuteAsync(Route<Guid>("competitionId"), request.InvitationToken, user.UserId, DateTimeOffset.UtcNow, ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, detail: result.ErrorMessage);
    }
}

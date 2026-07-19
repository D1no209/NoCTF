using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Membership;

namespace NoCTF.API.Endpoints.Teams;

public sealed class TransferTeamCaptainRequest { public Guid NewCaptainId { get; set; } }

public sealed class TransferTeamCaptainEndpoint(TransferTeamCaptain transfer, IUserContext user)
    : Endpoint<TransferTeamCaptainRequest, Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Post("/competitions/{competitionId}/teams/{teamId}/captain"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(TransferTeamCaptainRequest request, CancellationToken ct)
    {
        var result = await transfer.ExecuteAsync(Route<Guid>("competitionId"), Route<Guid>("teamId"), user.UserId, request.NewCaptainId, ct);
        if (result.ErrorCode is "team_not_found" or "member_not_found") return TypedResults.NotFound();
        if (result.ErrorCode == "captain_only") return TypedResults.Forbid();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Captain was not transferred.", detail: result.ErrorMessage);
        return TypedResults.NoContent();
    }
}

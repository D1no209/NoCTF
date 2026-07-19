using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionEndpoint(UpdateCompetition update, ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : Endpoint<UpdateCompetitionRequest, Results<Ok<CompetitionResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Put("/admin/competitions/{competitionId}"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<CompetitionResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(UpdateCompetitionRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct)) return TypedResults.Forbid();
        var result = await update.ExecuteAsync(CompetitionMapper.ToCommand(request, user.UserId, DateTimeOffset.UtcNow), ct);
        if (result.ErrorCode == "competition_not_found") return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Competition was not updated.", detail: result.ErrorMessage);
        return TypedResults.Ok(CompetitionMapper.ToResponse(result.Value!));
    }
}

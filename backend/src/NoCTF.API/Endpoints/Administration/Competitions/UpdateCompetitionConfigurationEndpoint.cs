using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionConfigurationEndpoint(UpdateCompetitionConfiguration update, ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : Endpoint<UpdateCompetitionConfigurationRequest, Results<Ok<CompetitionConfigurationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Put("/admin/competitions/{competitionId}/configuration"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<CompetitionConfigurationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(UpdateCompetitionConfigurationRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct)) return TypedResults.Forbid();
        var result = await update.ExecuteAsync(request.CompetitionId, request.ExpectedRevision, request.Json, DateTimeOffset.UtcNow, ct);
        if (result.ErrorCode == "competition_not_found") return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            var status = result.ErrorCode is "configuration_conflict" or "configuration_locked" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            return TypedResults.Problem(statusCode: status, title: "Competition configuration was not updated.", detail: result.ErrorMessage);
        }
        return TypedResults.Ok(CompetitionConfigurationMapper.ToResponse(result.Value!));
    }
}

using FastEndpoints; using Microsoft.AspNetCore.Http; using Microsoft.AspNetCore.Http.HttpResults; using NoCTF.API.Endpoints.Challenges; using NoCTF.API.Security; using NoCTF.Application.Challenges.Management; using NoCTF.Application.Teams.Moderation;
namespace NoCTF.API.Endpoints.Administration.Challenges;
public sealed class CreateChallengeEndpoint(CreateChallenge create, ICompetitionModerationAuthorizer authorizer, IUserContext user) : Endpoint<CreateChallengeRequest, Results<Created<ChallengeResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Post("/admin/competitions/{competitionId}/challenges"); AuthSchemes("Bearer"); Summary(s => { s.Summary = "Links a global challenge template to a competition."; s.Description = "Creates a CompetitionChallenge without copying or mutating the global template."; }); }
    public override async Task<Results<Created<ChallengeResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(CreateChallengeRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId"); if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct)) return TypedResults.Forbid();
        var result = await create.ExecuteAsync(ChallengeMapper.ToCommand(request, DateTimeOffset.UtcNow), ct); if (result.ErrorCode is "competition_not_found" or "challenge_template_not_found") return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: result.ErrorCode == "challenge_order_conflict" ? 409 : 400, title: "Challenge was not created.", detail: result.ErrorMessage);
        var response = ChallengeMapper.ToResponse(result.Value!); return TypedResults.Created($"/api/v1/admin/competitions/{request.CompetitionId}/challenges/{response.Id}", response);
    }
}

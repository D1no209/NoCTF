using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class GetChallengeHintRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid HintId { get; set; }
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}

public sealed class GetChallengeHintEndpoint(
    ManageChallengeHints hints,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetChallengeHintRequest,
        Results<Ok<ChallengeHintResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetitionChallengeHint"));
        Summary(summary =>
        {
            summary.Summary = "Gets a competition challenge hint.";
            summary.Description = "Returns full hint content and publication metadata to authorized observers.";
        });
    }

    public override async Task<Results<Ok<ChallengeHintResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        GetChallengeHintRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await hints.GetAsync(
            competitionId,
            request.CompetitionChallengeId,
            request.HintId,
            request.IncludeDeleted,
            ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeHintMapping.ToResponse(result));
    }
}

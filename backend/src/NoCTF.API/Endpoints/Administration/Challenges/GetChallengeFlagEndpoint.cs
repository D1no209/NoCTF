using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class GetCompetitionChallengeFlagRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid FlagId { get; set; }
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}

public sealed class GetChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetCompetitionChallengeFlagRequest,
        Results<Ok<ChallengeFlagResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{flagId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetitionChallengeFlag"));
        Summary(summary =>
        {
            summary.Summary = "Gets a competition-scoped flag.";
            summary.Description = "Returns protected flag material to authorized competition observers.";
        });
    }

    public override async Task<Results<Ok<ChallengeFlagResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        GetCompetitionChallengeFlagRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await flags.GetAsync(
            ChallengeFlagScope.Competition(competitionId, request.CompetitionChallengeId),
            request.FlagId,
            actorId: null,
            isAdministrator: true,
            request.IncludeDeleted,
            ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeFlagMapping.ToResponse(result));
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class ListCompetitionChallengeFlagsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}

public sealed class ListChallengeFlagsEndpoint(
    ManageChallengeFlags flags,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListCompetitionChallengeFlagsRequest,
        Results<Ok<ChallengeFlagListResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitionChallengeFlags"));
        Summary(summary =>
        {
            summary.Summary = "Lists competition-scoped flags.";
            summary.Description = "Returns protected flag records for one CompetitionChallenge to authorized observers.";
        });
    }

    public override async Task<Results<Ok<ChallengeFlagListResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        ListCompetitionChallengeFlagsRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var items = await flags.ListAsync(
            ChallengeFlagScope.Competition(competitionId, request.CompetitionChallengeId),
            actorId: null,
            isAdministrator: true,
            request.IncludeDeleted,
            ct);
        if (items is null)
            return TypedResults.NotFound();
        var supportsRegularExpression = await flags.SupportsRegularExpressionAsync(
            ChallengeFlagScope.Competition(competitionId, request.CompetitionChallengeId),
            actorId: null,
            isAdministrator: true,
            ct) == true;
        return TypedResults.Ok(new ChallengeFlagListResponse(
            items.Select(ChallengeFlagMapping.ToResponse).ToArray(),
            supportsRegularExpression));
    }
}

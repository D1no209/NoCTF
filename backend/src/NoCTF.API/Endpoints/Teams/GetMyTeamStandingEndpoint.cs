using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Standings;

namespace NoCTF.API.Endpoints.Teams;

public sealed class GetMyTeamStandingRequest { public Guid CompetitionId { get; set; } }
public sealed class GetMyTeamStandingValidator : Validator<GetMyTeamStandingRequest>
{
    public GetMyTeamStandingValidator() => RuleFor(request => request.CompetitionId).NotEmpty();
}
public sealed record MyTeamStandingResponse(Guid TeamId, int? Rank, long? TotalScore,
    IReadOnlyList<ScoreboardChallengeBenefit> ChallengeBenefits, string Version,
    DateTimeOffset GeneratedAt, DateTimeOffset? DataAsOf,
    LeaderboardVisibilityProtocol Visibility, LeaderboardDataScopeProtocol DataScope);

public sealed class GetMyTeamStandingEndpoint(GetMyTeamStanding get, IUserContext user)
    : Endpoint<GetMyTeamStandingRequest, Results<Ok<MyTeamStandingResponse>,
        Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/teams/me/standing");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("GetMyTeamStanding")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable));
    }

    public override async Task<Results<Ok<MyTeamStandingResponse>, Accepted<LeaderboardProcessingProtocolResponse>,
        NotFound, ProblemHttpResult>> ExecuteAsync(GetMyTeamStandingRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        HttpContext.Response.Headers.Vary = "Authorization";
        var result = await get.ExecuteAsync(request.CompetitionId, user.UserId, ct);
        if (result.State == MyTeamStandingState.Ready && result.Standing is { } value)
            return TypedResults.Ok(new MyTeamStandingResponse(value.TeamId, value.Rank, value.TotalScore,
                value.ChallengeBenefits, value.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                value.GeneratedAt, value.DataAsOf, CompetitionProtocolMapper.ToProtocol(value.Visibility),
                ScoreboardProtocolMapper.ToProtocol(value.DataScope)));
        if (result.State == MyTeamStandingState.Processing)
        {
            HttpContext.Response.Headers.RetryAfter = "2";
            var url = $"/api/v1/competitions/{request.CompetitionId}/teams/me/standing";
            return TypedResults.Accepted(url, new LeaderboardProcessingProtocolResponse(
                request.CompetitionId, LeaderboardProjectionStateProtocol.Processing, url));
        }
        if (result.State == MyTeamStandingState.ProjectionFailed)
            return ApiProblems.Problem(StatusCodes.Status503ServiceUnavailable,
                ApiMessages.Get(ApiMessageId.GetLeaderboardTitleLeaderboardProjectionUnavailable),
                extensions: new Dictionary<string, object?> { ["code"] = LeaderboardProblemCode.LeaderboardProjectionFailed });
        return TypedResults.NotFound();
    }
}

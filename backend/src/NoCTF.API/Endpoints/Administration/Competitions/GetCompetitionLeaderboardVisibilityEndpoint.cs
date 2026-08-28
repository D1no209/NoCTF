using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed record CompetitionLeaderboardVisibilityResponse(
    Guid CompetitionId,
    LeaderboardVisibilityProtocol EffectiveVisibility,
    DateTimeOffset? FrozenStartAt,
    DateTimeOffset? HiddenStartAt);

internal static class CompetitionLeaderboardVisibilityMapper
{
    public static CompetitionLeaderboardVisibilityResponse ToResponse(
        CompetitionVisibilityConfigurationView view) =>
        new(
            view.CompetitionId,
            CompetitionProtocolMapper.ToProtocol(view.EffectiveVisibility),
            view.FrozenStartAt,
            view.HiddenStartAt);
}

public sealed class GetCompetitionLeaderboardVisibilityEndpoint(
    GetCompetitionVisibility get,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<
        Ok<CompetitionLeaderboardVisibilityResponse>,
        NotFound,
        ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/leaderboard-visibility");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetitionLeaderboardVisibility"));
        Summary(summary =>
        {
            summary.Summary = "Gets the scheduled and effective leaderboard visibility policy.";
            summary.Description = "Returns the Normal, Frozen, or Blackout configuration for a competition.";
        });
    }

    public override async Task<Results<
        Ok<CompetitionLeaderboardVisibilityResponse>,
        NotFound,
        ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var view = await get.ExecuteAsync(competitionId, timeProvider.GetUtcNow(), ct);
        return view is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CompetitionLeaderboardVisibilityMapper.ToResponse(view));
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.API.Security;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class GetLeaderboardRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class GetLeaderboardEndpoint(
    ILeaderboardCache leaderboard,
    IBackendMessagePublisher messages,
    ICompetitionVisibilityAccess access,
    IUserContext user)
    : Endpoint<GetLeaderboardRequest, Results<Ok<LeaderboardResponse>, Accepted<LeaderboardProcessingResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard");
        AllowAnonymous();
        Summary(s => s.Summary = "Get the cached leaderboard or queue an asynchronous refresh.");
    }

    public override async Task<Results<Ok<LeaderboardResponse>, Accepted<LeaderboardProcessingResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetLeaderboardRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var visibility = await access.ResolveAsync(
            user.UserId,
            request.CompetitionId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (visibility is null)
            return TypedResults.NotFound();
        if (visibility.DataScope == LeaderboardDataScope.Hidden)
        {
            return TypedResults.Ok(new LeaderboardResponse(
                request.CompetitionId,
                DateTimeOffset.UtcNow,
                [])
            {
                Visibility = visibility.Visibility,
                DataScope = LeaderboardDataScope.Hidden
            });
        }
        var snapshot = visibility.DataScope == LeaderboardDataScope.Frozen
            ? await leaderboard.GetFrozenAsync(request.CompetitionId, cancellationToken)
            : await leaderboard.GetAsync(request.CompetitionId, cancellationToken);
        if (snapshot is not null)
        {
            if (visibility.DataScope == LeaderboardDataScope.Live && snapshot.Stale)
                await messages.ProjectLeaderboardAsync(request.CompetitionId, cancellationToken);
            return TypedResults.Ok(snapshot with
            {
                Visibility = visibility.Visibility,
                DataScope = visibility.DataScope,
                DataAsOf = visibility.DataScope == LeaderboardDataScope.Frozen
                    ? snapshot.DataAsOf
                    : snapshot.GeneratedAt
            });
        }
        if (visibility.DataScope == LeaderboardDataScope.Frozen)
        {
            await messages.ApplyCompetitionVisibilityAsync(
                request.CompetitionId,
                visibility.VisibilityRevision,
                cancellationToken);
            return Processing(request.CompetitionId, visibility.LeaderboardRevision);
        }
        var status = await leaderboard.GetStatusAsync(request.CompetitionId, cancellationToken);
        if (status.LastFailureAt is not null)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Leaderboard projection is unavailable.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "leaderboard_projection_failed",
                    ["targetRevision"] = status.TargetRevision,
                    ["lastFailureAt"] = status.LastFailureAt
                });
        await messages.ProjectLeaderboardAsync(request.CompetitionId, cancellationToken);
        return Processing(request.CompetitionId, status.TargetRevision);
    }

    private Accepted<LeaderboardProcessingResponse> Processing(
        Guid competitionId,
        long targetRevision)
    {
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{competitionId}/leaderboard";
        return TypedResults.Accepted(statusUrl, new LeaderboardProcessingResponse(
            competitionId,
            LeaderboardProjectionState.Processing,
            targetRevision,
            statusUrl));
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.WriteUps;

namespace NoCTF.API.Endpoints.Teams.WriteUps;

public sealed record TeamWriteUpChallengeScoreResponse(
    Guid CompetitionChallengeId,
    string Title,
    string Direction,
    long NetPoints);

public sealed record TeamWriteUpReviewItemResponse(
    TeamWriteUpResponse WriteUp,
    long? TotalScore,
    IReadOnlyList<TeamWriteUpChallengeScoreResponse> ChallengeScores);

public sealed record TeamWriteUpReviewResponse(
    bool ScoreboardAvailable,
    bool CanJudge,
    IReadOnlyList<TeamWriteUpReviewItemResponse> Items);

public sealed class ListTeamWriteUpsEndpoint(
    ManageTeamWriteUps writeUps,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<TeamWriteUpReviewResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/writeups");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("ListTeamWriteUps"));
        Summary(summary =>
        {
            summary.Summary = "Lists submitted team WriteUps with authoritative challenge scores.";
            summary.Description = "Competition owners, managers, judges, observers and platform administrators may review. Only judge-level callers may change scores or start consultations.";
        });
    }

    public override async Task<Results<Ok<TeamWriteUpReviewResponse>, ForbidHttpResult>>
        ExecuteAsync(CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(
                user.UserId,
                competitionId,
                cancellationToken))
        {
            return TypedResults.Forbid();
        }

        var canJudge = await authorizer.CanJudgeAsync(
            user.UserId,
            competitionId,
            cancellationToken);
        var review = await writeUps.ReviewAsync(competitionId, cancellationToken);
        return TypedResults.Ok(new TeamWriteUpReviewResponse(
            review.ScoreboardAvailable,
            canJudge,
            review.Items.Select(item => new TeamWriteUpReviewItemResponse(
                TeamWriteUpProtocol.ToResponse(item.WriteUp),
                item.TotalScore,
                item.ChallengeScores.Select(score => new TeamWriteUpChallengeScoreResponse(
                    score.CompetitionChallengeId,
                    score.Title,
                    score.Direction,
                    score.NetPoints)).ToArray())).ToArray()));
    }
}

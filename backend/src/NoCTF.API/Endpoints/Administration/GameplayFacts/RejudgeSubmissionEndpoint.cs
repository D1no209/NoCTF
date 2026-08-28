using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Management;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed record RejudgeGameplayFactResponse(Guid GameplayFactId, DateTimeOffset Cutoff);

public sealed class RejudgeGameplayFactEndpoint(
    QueueGameplayFactWork queue,
    IAdminGameplayFactStatusReader submissions,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<
        Results<Accepted<RejudgeGameplayFactResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/rejudge");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRejudgeGameplayFact"));
        Summary(summary =>
        {
            summary.Summary = "Queues one gameplay fact for exact rejudge.";
            summary.Description = "Reuses the original gameplay fact and archive without creating or consuming a new attempt.";
        });
    }

    public override async Task<Results<Accepted<RejudgeGameplayFactResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var gameplayFactId = Route<Guid>("gameplayFactId");
        var submission = await submissions.FindAsync(competitionId, gameplayFactId, ct);
        if (submission is null)
            return TypedResults.NotFound();
        var cutoff = timeProvider.GetUtcNow();
        await queue.QueueAsync(
            competitionId,
            submission.CompetitionChallengeId,
            cutoff,
            rejudge: true,
            gameplayFactId,
            ct);
        return TypedResults.Accepted(
            uri: (string?)null,
            value: new RejudgeGameplayFactResponse(gameplayFactId, cutoff));
    }
}

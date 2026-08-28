using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed class RejudgeGameplayFactsEndpoint(
    QueueGameplayFactWork queue,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<QueueGameplayFactWorkRequest,
        Results<Accepted<QueueGameplayFactWorkResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/gameplay-facts/rejudge");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRejudgeGameplayFacts"));
        Summary(summary =>
        {
            summary.Summary = "Queues a cutoff-bounded challenge rejudge.";
            summary.Description = "Schedules durable filtered rejudging without creating a batch business entity.";
        });
    }

    public override async Task<Results<Accepted<QueueGameplayFactWorkResponse>, ForbidHttpResult>> ExecuteAsync(
        QueueGameplayFactWorkRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var cutoff = timeProvider.GetUtcNow();
        await queue.QueueAsync(
            competitionId, request.CompetitionChallengeId, cutoff, rejudge: true, null, ct);
        return TypedResults.Accepted(
            uri: (string?)null,
            value: new QueueGameplayFactWorkResponse(request.CompetitionChallengeId, cutoff, true));
    }
}

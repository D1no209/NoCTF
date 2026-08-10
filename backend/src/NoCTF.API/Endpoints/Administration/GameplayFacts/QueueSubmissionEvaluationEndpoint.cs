using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed class QueueGameplayFactWorkRequest
{
    public Guid CompetitionChallengeId { get; set; }
}

public sealed class QueueGameplayFactWorkValidator : Validator<QueueGameplayFactWorkRequest>
{
    public QueueGameplayFactWorkValidator() =>
        RuleFor(request => request.CompetitionChallengeId).NotEmpty();
}

public sealed record QueueGameplayFactWorkResponse(
    Guid CompetitionChallengeId,
    DateTimeOffset Cutoff,
    bool Rejudge);

public sealed class QueueGameplayFactEvaluationEndpoint(
    QueueGameplayFactWork queue,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<QueueGameplayFactWorkRequest,
        Results<Accepted<QueueGameplayFactWorkResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/gameplay-facts/queue-evaluation");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminQueueGameplayFactEvaluation"));
        Summary(summary =>
        {
            summary.Summary = "Queues pending gameplay facts for evaluation.";
            summary.Description = "Creates cutoff-bounded durable work for eligible ManualBatch or first-failure gameplay facts.";
        });
    }

    public override async Task<Results<Accepted<QueueGameplayFactWorkResponse>, ForbidHttpResult>> ExecuteAsync(
        QueueGameplayFactWorkRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var cutoff = DateTimeOffset.UtcNow;
        await queue.QueueAsync(
            competitionId, request.CompetitionChallengeId, cutoff, rejudge: false, null, ct);
        return TypedResults.Accepted(
            uri: (string?)null,
            value: new QueueGameplayFactWorkResponse(request.CompetitionChallengeId, cutoff, false));
    }
}

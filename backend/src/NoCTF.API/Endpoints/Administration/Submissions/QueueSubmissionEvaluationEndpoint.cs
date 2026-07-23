using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Submissions;

public sealed class QueueSubmissionWorkRequest
{
    public Guid CompetitionChallengeId { get; set; }
}

public sealed class QueueSubmissionWorkValidator : Validator<QueueSubmissionWorkRequest>
{
    public QueueSubmissionWorkValidator() =>
        RuleFor(request => request.CompetitionChallengeId).NotEmpty();
}

public sealed record QueueSubmissionWorkResponse(
    Guid CompetitionChallengeId,
    DateTimeOffset Cutoff,
    bool Rejudge);

public sealed class QueueSubmissionEvaluationEndpoint(
    QueueSubmissionWork queue,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<QueueSubmissionWorkRequest,
        Results<Accepted<QueueSubmissionWorkResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/submissions/queue-evaluation");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Queues pending ManualBatch submissions for evaluation.");
    }

    public override async Task<Results<Accepted<QueueSubmissionWorkResponse>, ForbidHttpResult>> ExecuteAsync(
        QueueSubmissionWorkRequest request,
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
            value: new QueueSubmissionWorkResponse(request.CompetitionChallengeId, cutoff, false));
    }
}

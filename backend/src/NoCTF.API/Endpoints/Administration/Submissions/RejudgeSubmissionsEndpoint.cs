using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Submissions;

public sealed class RejudgeSubmissionsEndpoint(
    QueueSubmissionWork queue,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<QueueSubmissionWorkRequest,
        Results<Accepted<QueueSubmissionWorkResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/submissions/rejudge");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRejudgeSubmissions"));
        Summary(summary =>
        {
            summary.Summary = "Queues a cutoff-bounded challenge rejudge.";
            summary.Description = "Schedules durable filtered rejudging without creating a batch business entity.";
        });
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
            competitionId, request.CompetitionChallengeId, cutoff, rejudge: true, null, ct);
        return TypedResults.Accepted(
            uri: (string?)null,
            value: new QueueSubmissionWorkResponse(request.CompetitionChallengeId, cutoff, true));
    }
}

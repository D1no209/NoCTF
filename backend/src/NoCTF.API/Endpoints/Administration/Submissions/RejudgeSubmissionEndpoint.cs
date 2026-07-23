using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Management;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Submissions;

public sealed record RejudgeSubmissionResponse(Guid SubmissionId, DateTimeOffset Cutoff);

public sealed class RejudgeSubmissionEndpoint(
    QueueSubmissionWork queue,
    IAdminSubmissionStatusReader submissions,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<Accepted<RejudgeSubmissionResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/submissions/{submissionId}/rejudge");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Queues one submission for exact rejudge.");
    }

    public override async Task<Results<Accepted<RejudgeSubmissionResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var submissionId = Route<Guid>("submissionId");
        var submission = await submissions.FindAsync(competitionId, submissionId, ct);
        if (submission is null)
            return TypedResults.NotFound();
        var cutoff = DateTimeOffset.UtcNow;
        await queue.QueueAsync(
            competitionId,
            submission.CompetitionChallengeId,
            cutoff,
            rejudge: true,
            submissionId,
            ct);
        return TypedResults.Accepted(
            uri: (string?)null,
            value: new RejudgeSubmissionResponse(submissionId, cutoff));
    }
}

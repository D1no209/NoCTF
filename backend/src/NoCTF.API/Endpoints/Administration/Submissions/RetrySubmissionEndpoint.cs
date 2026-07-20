using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Retry;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Submissions;

public sealed class RetrySubmissionRequest
{
    public Guid CompetitionId { get; set; }
    public Guid SubmissionId { get; set; }
}

public sealed class RetrySubmissionEndpoint(
    RetrySubmission retry,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<RetrySubmissionRequest, Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/submissions/{submissionId}/retry");
        AuthSchemes("Bearer");
        Summary(s => s.Summary = "Retry a non-correct submission.");
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        RetrySubmissionRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.SubmissionId = Route<Guid>("submissionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, request.CompetitionId, cancellationToken))
            return TypedResults.Forbid();
        var result = await retry.ExecuteAsync(request.CompetitionId, request.SubmissionId, user.UserId, false, cancellationToken);
        if (result.ErrorCode is "submission_not_found" or "competition_not_found") return TypedResults.NotFound();
        return result.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Submission cannot be retried.", detail: result.ErrorMessage);
    }
}

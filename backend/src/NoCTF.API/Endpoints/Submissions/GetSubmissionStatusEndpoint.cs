using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Ports;

namespace NoCTF.API.Endpoints.Submissions;

public sealed class GetSubmissionStatusRequest
{
    public Guid CompetitionId { get; set; }
    public Guid SubmissionId { get; set; }
}

public sealed class GetSubmissionStatusEndpoint(
    ISubmissionStatusReader statusReader,
    IUserContext userContext) : Endpoint<GetSubmissionStatusRequest, Results<Ok<SubmissionStatusResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/submissions/{submissionId}");
        AuthSchemes("Bearer");
    }

    public override async Task<Results<Ok<SubmissionStatusResponse>, NotFound>> ExecuteAsync(
        GetSubmissionStatusRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.SubmissionId = Route<Guid>("submissionId");
        var result = await statusReader.FindAsync(
            request.CompetitionId,
            request.SubmissionId,
            userContext.UserId,
            cancellationToken);
        if (result is null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(new SubmissionStatusResponse(
            result.SubmissionId,
            result.Kind,
            result.Result is null ? "Unprocessed" : result.Result == NoCTF.Domain.Submissions.ScoringResult.Correct ? "Success" : "Failure",
            result.ReceivedAt,
            result.CompletedAt,
            result.FailureCode));
    }
}

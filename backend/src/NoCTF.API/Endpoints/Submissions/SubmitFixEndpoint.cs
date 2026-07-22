using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Intake;

namespace NoCTF.API.Endpoints.Submissions;

public sealed class SubmitFixRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid UploadId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class SubmitFixEndpoint(
    SubmitFix submitFix,
    IUserContext userContext,
    IHttpContextAccessor httpContextAccessor) : Endpoint<SubmitFixRequest,
        Microsoft.AspNetCore.Http.HttpResults.Results<Accepted<AcceptedSubmissionResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/submissions/fixes");
        AuthSchemes("Bearer");
        Options(options => options.WithMetadata(new EnableRateLimitingAttribute("submission")));
        Description(builder => builder.ProducesProblemFE(StatusCodes.Status400BadRequest)
            .ProducesProblemFE(StatusCodes.Status403Forbidden)
            .ProducesProblemFE(StatusCodes.Status409Conflict)
            .ProducesProblemFE(StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Accept a Fix archive reference for asynchronous processing.");
    }

    public override async Task<Microsoft.AspNetCore.Http.HttpResults.Results<Accepted<AcceptedSubmissionResponse>, ProblemHttpResult>> ExecuteAsync(
        SubmitFixRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await submitFix.ExecuteAsync(new(
            request.CompetitionId, request.TeamId, request.CompetitionChallengeId, userContext.UserId, request.UploadId, request.IdempotencyKey,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown", DateTimeOffset.UtcNow), cancellationToken);
        if (!result.Succeeded)
        {
            return SubmissionProblemDetails.Create(SubmissionProblemDetails.StatusFor(result.ErrorCode), result.ErrorCode, result.ErrorMessage);
        }
        return TypedResults.Accepted<AcceptedSubmissionResponse>(
            uri: (string?)null,
            value: SubmissionMapper.ToResponse(result.Value!));
    }
}

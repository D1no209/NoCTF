using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Intake;

namespace NoCTF.API.Endpoints.Submissions;

public sealed class SubmitFlagRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public string Flag { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? TargetTeamId { get; set; }
    public Guid? ServiceId { get; set; }
    public Guid? StageId { get; set; }
}

public sealed class SubmitFlagEndpoint(
    SubmitFlag submitFlag,
    IUserContext userContext,
    IHttpContextAccessor httpContextAccessor) : Endpoint<SubmitFlagRequest,
        Microsoft.AspNetCore.Http.HttpResults.Results<Accepted<AcceptedSubmissionResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/submissions/flags");
        AuthSchemes("Bearer");
        Options(options => options.WithMetadata(new EnableRateLimitingAttribute("submission")));
        Description(builder => builder.ProducesProblemFE(StatusCodes.Status400BadRequest)
            .ProducesProblemFE(StatusCodes.Status403Forbidden)
            .ProducesProblemFE(StatusCodes.Status409Conflict)
            .ProducesProblemFE(StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Accept a Flag submission for asynchronous processing.");
    }

    public override async Task<Microsoft.AspNetCore.Http.HttpResults.Results<Accepted<AcceptedSubmissionResponse>, ProblemHttpResult>> ExecuteAsync(
        SubmitFlagRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await submitFlag.ExecuteAsync(new(
            request.CompetitionId, request.TeamId, request.ChallengeId, userContext.UserId, request.Flag, request.IdempotencyKey,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            DateTimeOffset.UtcNow,
            request.TargetTeamId is null && request.ServiceId is null
                ? null
                : new(request.TargetTeamId ?? Guid.Empty, request.ServiceId ?? Guid.Empty),
            request.StageId), cancellationToken);
        if (!result.Succeeded)
        {
            return SubmissionProblemDetails.Create(SubmissionProblemDetails.StatusFor(result.ErrorCode), result.ErrorCode, result.ErrorMessage);
        }
        return TypedResults.Accepted<AcceptedSubmissionResponse>(
            uri: (string?)null,
            value: SubmissionMapper.ToResponse(result.Value!));
    }

}

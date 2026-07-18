using FastEndpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Intake;

namespace NoCTF.API.Endpoints.Submissions;

public sealed class SubmitFlagEndpoint(
    SubmitFlag submitFlag,
    IUserContext userContext,
    IHttpContextAccessor httpContextAccessor) : Endpoint<SubmitFlagRequest, AcceptedSubmissionResponse>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/submissions/flags");
        AuthSchemes("Bearer");
        Options(options => options.WithMetadata(new EnableRateLimitingAttribute("submission")));
        Summary(summary => summary.Summary = "Accept a Flag submission for asynchronous processing.");
    }

    public override async Task HandleAsync(SubmitFlagRequest request, CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await submitFlag.ExecuteAsync(new(
            request.CompetitionId,
            request.TeamId,
            request.ChallengeId,
            userContext.UserId,
            request.Flag,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            DateTimeOffset.UtcNow), cancellationToken);
        if (!result.Succeeded)
        {
            await SubmissionProblemDetails.WriteAsync(HttpContext.Response, MapStatus(result.ErrorCode),
                result.ErrorCode, result.ErrorMessage, cancellationToken);
            return;
        }
        await HttpContext.Response.SendAsync<AcceptedSubmissionResponse>(new(result.Value!.SubmissionId, result.Value.ReceivedAt), StatusCodes.Status202Accepted, null, cancellationToken);
    }

    private static int MapStatus(string? code) => code switch
    {
        "team_banned" or "team_forbidden" => StatusCodes.Status403Forbidden,
        "competition_finished" or "competition_not_started" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };
}

using FastEndpoints;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Intake;

namespace NoCTF.API.Endpoints.Submissions;

public sealed class SubmitFixEndpoint(
    SubmitFix submitFix,
    IUserContext userContext,
    IHttpContextAccessor httpContextAccessor) : Endpoint<SubmitFixRequest, AcceptedSubmissionResponse>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/submissions/fixes");
        AuthSchemes("Bearer");
        Options(options => options.WithMetadata(new EnableRateLimitingAttribute("submission")));
        Summary(summary => summary.Summary = "Accept a Fix archive reference for asynchronous processing.");
    }

    public override async Task HandleAsync(SubmitFixRequest request, CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await submitFix.ExecuteAsync(new(
            request.CompetitionId,
            request.TeamId,
            request.ChallengeId,
            userContext.UserId,
            new FixArchiveReference(request.ObjectKey, request.FileName, request.ContentType, request.Length, request.Sha256),
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            DateTimeOffset.UtcNow), cancellationToken);
        if (!result.Succeeded)
        {
            await SubmissionProblemDetails.WriteAsync(HttpContext.Response, StatusCodes.Status400BadRequest,
                result.ErrorCode, result.ErrorMessage, cancellationToken);
            return;
        }
        await HttpContext.Response.SendAsync<AcceptedSubmissionResponse>(new(result.Value!.SubmissionId, result.Value.ReceivedAt), StatusCodes.Status202Accepted, null, cancellationToken);
    }
}

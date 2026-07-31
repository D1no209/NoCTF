using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Intake;

namespace NoCTF.API.Endpoints.Submissions;

public sealed class SubmitFixRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid PatchUploadId { get; set; }
}

public sealed class SubmitFixRequestValidator : Validator<SubmitFixRequest>
{
    public SubmitFixRequestValidator() =>
        RuleFor(request => request.PatchUploadId).NotEmpty();
}

public sealed class SubmitFixEndpoint(SubmitFix submitFix, IUserContext userContext)
    : Endpoint<SubmitFixRequest,
        Results<Accepted<AcceptedSubmissionResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/fix-submissions");
        AuthSchemes("Bearer");
        Options(options => options
            .WithMetadata(new EnableRateLimitingAttribute("submission"))
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status403Forbidden)
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests));
        Summary(summary =>
        {
            summary.Summary = "Consume a PatchUpload and create a Fix attempt.";
            summary.Description = "PatchUpload consumption and durable evaluation dispatch are atomic.";
        });
    }

    public override async Task<Results<Accepted<AcceptedSubmissionResponse>, ProblemHttpResult>> ExecuteAsync(
        SubmitFixRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.CompetitionChallengeId = Route<Guid>("competitionChallengeId");
        var result = await submitFix.ExecuteAsync(new(
            request.CompetitionId,
            request.CompetitionChallengeId,
            userContext.UserId,
            request.PatchUploadId,
            DateTimeOffset.UtcNow), cancellationToken);
        if (!result.Succeeded)
            return SubmissionProblemDetails.Create(
                SubmissionProblemDetails.StatusFor(result.ErrorCode),
                result.ErrorCode,
                result.ErrorMessage);
        return TypedResults.Accepted<AcceptedSubmissionResponse>(
            $"/api/v1/competitions/{request.CompetitionId}/submissions/{result.Value!.SubmissionId}",
            SubmissionMapper.ToResponse(result.Value));
    }
}

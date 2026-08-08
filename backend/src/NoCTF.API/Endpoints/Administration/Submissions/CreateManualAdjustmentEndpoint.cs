using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Submissions;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Submissions;

public sealed class CreateManualAdjustmentRequest
{
    public Guid TeamId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public int Delta { get; set; }
}

public sealed class CreateManualAdjustmentValidator : Validator<CreateManualAdjustmentRequest>
{
    public CreateManualAdjustmentValidator()
    {
        RuleFor(request => request.TeamId).NotEmpty();
        RuleFor(request => request.CompetitionChallengeId).NotEmpty();
        RuleFor(request => request.Delta).NotEqual(0);
    }
}

public sealed class CreateManualAdjustmentEndpoint(
    CreateManualAdjustment create,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<CreateManualAdjustmentRequest,
        Results<Accepted<AcceptedSubmissionResponse>, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/submissions/manual-adjustments");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Queues a signed manual score adjustment.";
            summary.Description = "The delta is persisted as the canonical signed Int32 SubmittedFlag on a ManualAdjust submission.";
        });
    }

    public override async Task<Results<Accepted<AcceptedSubmissionResponse>, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        CreateManualAdjustmentRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await create.ExecuteAsync(new(
            competitionId,
            request.TeamId,
            request.CompetitionChallengeId,
            request.Delta,
            user.UserId,
            DateTimeOffset.UtcNow), ct);
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Manual adjustment was not accepted.",
                detail: result.ErrorMessage);
        var accepted = result.Value!;
        return TypedResults.Accepted(
            uri: $"/api/v1/competitions/{competitionId}/submissions/{accepted.SubmissionId}",
            value: new AcceptedSubmissionResponse(accepted.SubmissionId, accepted.ReceivedAt));
    }
}

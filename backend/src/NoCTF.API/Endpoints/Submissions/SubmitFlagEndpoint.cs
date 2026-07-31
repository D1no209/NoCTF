using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Status;
using NoCTF.Domain.Submissions;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Submissions;

public sealed record AcceptedSubmissionResponse(Guid SubmissionId, DateTimeOffset ReceivedAt);

public sealed record SubmissionStatusResponse(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    SubmissionKind Kind,
    SubmissionEvaluationState EvaluationState,
    ScoringResult? Result,
    ScoringFailureCode? FailureCode,
    DateTimeOffset ReceivedAt,
    DateTimeOffset EvaluationUpdatedAt,
    long ProcessingVersion);

public sealed record AdminSubmissionStatusResponse(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    Guid SubmittedByUserId,
    SubmissionKind Kind,
    SubmissionEvaluationState EvaluationState,
    ScoringResult? Result,
    ScoringFailureCode? FailureCode,
    DateTimeOffset ReceivedAt,
    DateTimeOffset EvaluationUpdatedAt,
    long ProcessingVersion);

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class SubmissionMapper
{
    public static partial AcceptedSubmissionResponse ToResponse(SubmissionAccepted accepted);
    public static partial SubmissionStatusResponse ToStatusResponse(SubmissionStatusView view);
    public static partial AdminSubmissionStatusResponse ToAdminStatusResponse(
        AdminSubmissionStatusView view);
}

internal static class SubmissionProblemDetails
{
    public static int StatusFor(string? code) => code switch
    {
        "team_banned" or "team_forbidden" => StatusCodes.Status403Forbidden,
        "competition_finished" or "competition_not_started" or "break_required" =>
            StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };

    public static ProblemHttpResult Create(int status, string? code, string? detail) =>
        TypedResults.Problem(
            statusCode: status,
            title: "Submission was not accepted.",
            detail: detail,
            type: "https://httpstatuses.com/" + status,
            extensions: string.IsNullOrWhiteSpace(code)
                ? null
                : new Dictionary<string, object?> { ["code"] = code });
}

public sealed class SubmitFlagRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public string? Flag { get; set; }
    public IReadOnlyList<string>? Flags { get; set; }
}

public sealed class SubmitFlagRequestValidator : Validator<SubmitFlagRequest>
{
    public SubmitFlagRequestValidator()
    {
        RuleFor(request => request)
            .Must(request => request.Flag is not null ^ request.Flags is not null)
            .WithMessage("Exactly one of flag or flags is required.");
        RuleForEach(request => request.Flags)
            .NotNull()
            .SwaggerIgnore();
    }
}

public sealed record FlagSubmissionItem(Guid SubmissionId, string StatusUrl);

public sealed record FlagSubmissionAcceptedResponse(
    Guid? SubmissionId,
    string? StatusUrl,
    IReadOnlyList<FlagSubmissionItem>? Submissions);

public sealed class SubmitFlagEndpoint(SubmitFlag submitFlag, IUserContext userContext)
    : Endpoint<SubmitFlagRequest,
        Results<Accepted<FlagSubmissionAcceptedResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions");
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
            summary.Summary = "Submit one Flag or an ordered AWD Flag collection.";
            summary.Description =
                "Creates independent immutable attempts. The accepted response is not an evaluation result.";
        });
    }

    public override async Task<Results<Accepted<FlagSubmissionAcceptedResponse>, ProblemHttpResult>> ExecuteAsync(
        SubmitFlagRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.CompetitionChallengeId = Route<Guid>("competitionChallengeId");
        var values = request.Flags ?? [request.Flag!];
        var result = await submitFlag.ExecuteBatchAsync(
            request.CompetitionId,
            request.CompetitionChallengeId,
            userContext.UserId,
            values,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (!result.Succeeded)
            return SubmissionProblemDetails.Create(
                SubmissionProblemDetails.StatusFor(result.ErrorCode),
                result.ErrorCode,
                result.ErrorMessage);
        var accepted = new List<FlagSubmissionItem>(values.Count);
        foreach (var submission in result.Value!)
        {
            var statusUrl =
                $"/api/v1/competitions/{request.CompetitionId}/submissions/{submission.SubmissionId}";
            accepted.Add(new(submission.SubmissionId, statusUrl));
        }

        var body = request.Flags is null
            ? new FlagSubmissionAcceptedResponse(
                accepted[0].SubmissionId, accepted[0].StatusUrl, null)
            : new FlagSubmissionAcceptedResponse(null, null, accepted);
        return TypedResults.Accepted<FlagSubmissionAcceptedResponse>((string?)null, body);
    }
}

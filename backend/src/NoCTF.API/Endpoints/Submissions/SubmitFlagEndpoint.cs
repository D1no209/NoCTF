using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Status;
using NoCTF.Domain.Submissions;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Submissions;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<SubmissionKindProtocol>))]
public enum SubmissionKindProtocol
{
    Flag,
    Break,
    Fix
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<SubmissionEvaluationStateProtocol>))]
public enum SubmissionEvaluationStateProtocol
{
    Pending,
    Queued,
    Processing,
    Completed,
    PlatformFailed
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoringResultProtocol>))]
public enum ScoringResultProtocol
{
    Correct,
    Wrong,
    Duplicate,
    AttemptsExhausted,
    PlatformFailed,
    Rejected
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoringFailureCodeProtocol>))]
public enum ScoringFailureCodeProtocol
{
    FlagNotSupported,
    FixNotSupported,
    BreakAttemptsExhausted,
    FixAttemptsExhausted,
    BreakRequired,
    ArchiveValidationUnavailable,
    FixArchiveMissing,
    FixArchiveLengthMismatch,
    FixArchiveContentTypeMismatch,
    FixArchiveHashMismatch,
    StorageTimeout,
    StorageUnavailable,
    CheckerPlatformError,
    SelfAttackRejected,
    DuplicateAttack,
    DuplicateAchievement,
    UnknownTeamIdentifier,
    InvalidObservation,
    ProducerTimeout,
    ProducerUnavailable,
    AmbiguousFlagMatch,
    FlagExpired,
    RoundOutOfRange,
    HardeningActive,
    AwdpFixFailed,
    AwdpPatchFailed,
    AwdpPatchTimeout,
    AwdpServiceDown,
    AwdpViolation,
    ForeignTeamFlagDetected
}

public sealed record AcceptedSubmissionResponse(Guid SubmissionId, DateTimeOffset ReceivedAt);

public sealed record SubmissionStatusResponse(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    SubmissionKindProtocol Kind,
    SubmissionEvaluationStateProtocol EvaluationState,
    ScoringResultProtocol? Result,
    ScoringFailureCodeProtocol? FailureCode,
    DateTimeOffset ReceivedAt,
    DateTimeOffset EvaluationUpdatedAt,
    long ProcessingVersion);

public sealed record AdminSubmissionStatusResponse(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    Guid SubmittedByUserId,
    SubmissionKindProtocol Kind,
    SubmissionEvaluationStateProtocol EvaluationState,
    ScoringResultProtocol? Result,
    ScoringFailureCodeProtocol? FailureCode,
    DateTimeOffset ReceivedAt,
    DateTimeOffset EvaluationUpdatedAt,
    long ProcessingVersion);

[JsonConverter(typeof(StrictPascalCaseEnumConverter<SubmissionAdmissionFailureCode>))]
public enum SubmissionAdmissionFailureCode
{
    ResourceDeleted,
    ChallengeUnavailable,
    TeamForbidden,
    TeamBanned,
    SubmissionKindUnsupported,
    CompetitionPaused,
    CompetitionNotPublished,
    CompetitionNotStarted,
    CompetitionFinished,
    CompetitionUnavailable,
    BreakRequired,
    AttemptsExhausted,
    FlagInvalid,
    FlagBatchNotSupported,
    SubmissionScopeNotFound,
    SubmissionConcurrency,
    PatchUploadNotFound
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class SubmissionMapper
{
    public static partial AcceptedSubmissionResponse ToResponse(SubmissionAccepted accepted);
    public static partial SubmissionStatusResponse ToStatusResponse(SubmissionStatusView view);
    public static partial AdminSubmissionStatusResponse ToAdminStatusResponse(
        AdminSubmissionStatusView view);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial SubmissionAdmissionFailureCode ToProtocol(
        SubmissionFailureCode failureCode);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial SubmissionKindProtocol ToProtocol(SubmissionKind value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial SubmissionEvaluationStateProtocol ToProtocol(
        SubmissionEvaluationState value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial ScoringResultProtocol ToProtocol(ScoringResult value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial ScoringFailureCodeProtocol ToProtocol(ScoringFailureCode value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial SubmissionKind ToDomain(SubmissionKindProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial SubmissionEvaluationState ToDomain(
        SubmissionEvaluationStateProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial ScoringResult ToDomain(ScoringResultProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial ScoringFailureCode ToDomain(ScoringFailureCodeProtocol value);
}

internal static class SubmissionProblemDetails
{
    public static int StatusFor(SubmissionFailureCode? code) => code switch
    {
        SubmissionFailureCode.TeamBanned or SubmissionFailureCode.TeamForbidden =>
            StatusCodes.Status403Forbidden,
        SubmissionFailureCode.CompetitionFinished or SubmissionFailureCode.CompetitionNotStarted
            or SubmissionFailureCode.BreakRequired =>
            StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };

    public static ProblemHttpResult Create(
        int status,
        SubmissionFailureCode? code,
        string? detail) =>
        TypedResults.Problem(
            statusCode: status,
            title: "Submission was not accepted.",
            detail: detail,
            type: "https://httpstatuses.com/" + status,
            extensions: code is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["code"] = SubmissionMapper.ToProtocol(code.Value)
                });
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
                SubmissionProblemDetails.StatusFor(result.FailureCode),
                result.FailureCode,
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

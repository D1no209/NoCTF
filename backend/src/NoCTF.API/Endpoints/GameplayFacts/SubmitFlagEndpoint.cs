using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Domain.Gameplay;
using AdmissionFailureCode = NoCTF.Application.GameplayFacts.Intake.GameplayFactAdmissionFailureCode;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.GameplayFacts;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<GameplayFactKindProtocol>))]
public enum GameplayFactKindProtocol
{
    FlagAttempt,
    BreakAttempt,
    FixAttempt,
    HintUnlock,
    ManualAdjustment,
    AwdServiceTransition,
    KohControlObservation
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<GameplayFactStateProtocol>))]
public enum GameplayFactStateProtocol
{
    Pending,
    Queued,
    Processing,
    Completed,
    PlatformFailed
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<GameplayFactResultProtocol>))]
public enum GameplayFactResultProtocol
{
    Correct,
    Wrong,
    Duplicate,
    AttemptsExhausted,
    Rejected,
    Unlocked,
    Applied,
    ServiceUp,
    ServiceDown,
    Controlled,
    Uncontrolled
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<GameplayFactFailureCodeProtocol>))]
public enum GameplayFactFailureCodeProtocol
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
    AwdpExploitSucceeded,
    AwdpPatchFailed,
    AwdpPatchTimeout,
    AwdpServiceAbnormal,
    AwdpPlatformFailed,
    AwdpViolation,
    ForeignTeamFlagDetected,
    InsufficientScore,
    HintUnavailable
}

public sealed record AcceptedGameplayFactResponse(
    Guid GameplayFactId,
    GameplayFactStateProtocol State,
    string StatusUrl);

public sealed record GameplayFactStatusResponse(
    Guid GameplayFactId,
    Guid CompetitionId,
    Guid? TeamId,
    Guid CompetitionChallengeId,
    GameplayFactKindProtocol Kind,
    GameplayFactStateProtocol State,
    GameplayFactResultProtocol? Result,
    GameplayFactFailureCodeProtocol? FailureCode,
    DateTimeOffset OccurredAt,
    DateTimeOffset UpdatedAt);

public sealed record AdminGameplayFactStatusResponse(
    Guid GameplayFactId,
    Guid CompetitionId,
    Guid? TeamId,
    Guid CompetitionChallengeId,
    Guid? ActorUserId,
    GameplayFactKindProtocol Kind,
    GameplayFactStateProtocol State,
    GameplayFactResultProtocol? Result,
    GameplayFactFailureCodeProtocol? FailureCode,
    DateTimeOffset OccurredAt,
    DateTimeOffset UpdatedAt,
    NoCTF.API.Endpoints.Administration.GameplayFacts.AdminPatchMetadataResponse? Patch = null,
    NoCTF.API.Endpoints.Administration.GameplayFacts.AdminPatchFailureCode? PatchFailure = null,
    bool CanDownloadPatch = false);

[JsonConverter(typeof(StrictPascalCaseEnumConverter<GameplayFactAdmissionFailureCodeProtocol>))]
public enum GameplayFactAdmissionFailureCodeProtocol
{
    ResourceDeleted,
    ChallengeUnavailable,
    TeamForbidden,
    TeamBanned,
    GameplayFactKindUnsupported,
    CompetitionPaused,
    CompetitionNotPublished,
    CompetitionNotStarted,
    CompetitionFinished,
    CompetitionUnavailable,
    BreakRequired,
    AchievementAlreadySucceeded,
    AttemptsExhausted,
    FlagInvalid,
    FlagBatchNotSupported,
    GameplayFactScopeNotFound,
    GameplayFactConcurrency,
    PatchUploadNotFound
}

public sealed record GameplayFactAdmissionFailureResponse(
    GameplayFactAdmissionFailureCodeProtocol Code,
    string? Detail);

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class GameplayFactMapper
{
    public static partial GameplayFactStatusResponse ToStatusResponse(GameplayFactStatusView view);
    [MapperIgnoreTarget(nameof(AdminGameplayFactStatusResponse.Patch))]
    [MapperIgnoreTarget(nameof(AdminGameplayFactStatusResponse.PatchFailure))]
    [MapperIgnoreTarget(nameof(AdminGameplayFactStatusResponse.CanDownloadPatch))]
    public static partial AdminGameplayFactStatusResponse ToAdminStatusResponse(
        AdminGameplayFactStatusView view);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameplayFactAdmissionFailureCodeProtocol ToProtocol(
        AdmissionFailureCode failureCode);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameplayFactKindProtocol ToProtocol(GameplayFactKind value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameplayFactStateProtocol ToProtocol(
        GameplayFactState value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameplayFactResultProtocol ToProtocol(GameplayFactResult value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameplayFactFailureCodeProtocol ToProtocol(GameplayFactFailureCode value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameplayFactKind ToDomain(GameplayFactKindProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameplayFactState ToDomain(
        GameplayFactStateProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameplayFactResult ToDomain(GameplayFactResultProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameplayFactFailureCode ToDomain(GameplayFactFailureCodeProtocol value);
}

internal static class GameplayFactProblemDetails
{
    public static int StatusFor(AdmissionFailureCode? code) => code switch
    {
        AdmissionFailureCode.TeamBanned or AdmissionFailureCode.TeamForbidden =>
            StatusCodes.Status403Forbidden,
        AdmissionFailureCode.CompetitionFinished or AdmissionFailureCode.CompetitionNotStarted
            or AdmissionFailureCode.BreakRequired
            or AdmissionFailureCode.AchievementAlreadySucceeded =>
            StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };

    public static ProblemHttpResult Create(
        int status,
        AdmissionFailureCode? code,
        string? detail) =>
        TypedResults.Problem(
            statusCode: status,
            title: "GameplayFact was not accepted.",
            detail: detail,
            type: "https://httpstatuses.com/" + status,
            extensions: code is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["code"] = GameplayFactMapper.ToProtocol(code.Value)
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
        RuleFor(request => request.Flags).Must(flags => flags is null || flags.Count <= 128)
            .WithMessage("每次最多提交 128 个 Flag。");
    }
}

public sealed record FlagGameplayFactItem(
    Guid GameplayFactId,
    GameplayFactStateProtocol State,
    string StatusUrl);

public sealed record FlagGameplayFactAcceptedResponse(
    Guid? GameplayFactId,
    GameplayFactStateProtocol? State,
    string? StatusUrl,
    IReadOnlyList<FlagGameplayFactItem>? Submissions);

public sealed class SubmitFlagEndpoint(SubmitFlag submitFlag, IUserContext userContext, TimeProvider timeProvider)
    : Endpoint<SubmitFlagRequest,
        Results<Accepted<FlagGameplayFactAcceptedResponse>,
            Conflict<GameplayFactAdmissionFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions");
        AuthSchemes("Bearer");
        MaxRequestBodySize(4 * 1024 * 1024);
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.FlagSubmission)));
        Options(options => options
            .WithMetadata(new EnableRateLimitingAttribute("submission"))
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests));
        Summary(summary =>
        {
            summary.Summary = "Submit one Flag or an ordered AWD Flag collection.";
            summary.Description =
                "Creates independent immutable attempts. The accepted response is not an evaluation result.";
        });
    }

    public override async Task<Results<Accepted<FlagGameplayFactAcceptedResponse>,
        Conflict<GameplayFactAdmissionFailureResponse>, ProblemHttpResult>> ExecuteAsync(
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
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (!result.Succeeded)
        {
            if (GameplayFactProblemDetails.StatusFor(result.FailureCode)
                == StatusCodes.Status409Conflict)
            {
                return TypedResults.Conflict(new GameplayFactAdmissionFailureResponse(
                    GameplayFactMapper.ToProtocol(result.FailureCode!.Value),
                    result.ErrorMessage));
            }
            return GameplayFactProblemDetails.Create(
                GameplayFactProblemDetails.StatusFor(result.FailureCode),
                result.FailureCode,
                result.ErrorMessage);
        }
        var accepted = new List<FlagGameplayFactItem>(values.Count);
        foreach (var submission in result.Value!)
        {
            var statusUrl =
                $"/api/v1/competitions/{request.CompetitionId}/gameplay-facts/{submission.GameplayFactId}";
            accepted.Add(new(submission.GameplayFactId, GameplayFactStateProtocol.Queued, statusUrl));
        }

        var body = request.Flags is null
            ? new FlagGameplayFactAcceptedResponse(
                accepted[0].GameplayFactId, accepted[0].State, accepted[0].StatusUrl, null)
            : new FlagGameplayFactAcceptedResponse(null, null, null, accepted);
        return TypedResults.Accepted<FlagGameplayFactAcceptedResponse>((string?)null, body);
    }
}

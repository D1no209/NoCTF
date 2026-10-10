using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Admission;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.GameplayFacts;

public sealed class UploadPatchVerificationRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid RuntimeInstanceId { get; set; }
    public IFormFile File { get; set; } = null!;
}

public sealed class UploadPatchVerificationValidator
    : Validator<UploadPatchVerificationRequest>
{
    public UploadPatchVerificationValidator()
    {
        RuleFor(request => request.RuntimeInstanceId).NotEmpty();
        RuleFor(request => request.File).NotNull();
    }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PatchVerificationUploadFailureCode>))]
public enum PatchVerificationUploadFailureCode
{
    ArchiveStreamNotSeekable,
    ArchiveTooLarge,
    ArchiveInvalid,
    TargetNotReady,
    PatchAlreadyVerified,
    AttemptsExhausted,
    TargetConsumed,
    UploadConflict
}

public sealed record PatchVerificationUploadFailureResponse(
    PatchVerificationUploadFailureCode Code,
    string Detail)
{
    public string Detail { get; init; } = ApiMessages.Localize(Code, Detail, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed class UploadPatchVerificationEndpoint(
    CreatePatchUpload upload,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<UploadPatchVerificationRequest,
        Results<Accepted<UploadPatchResponse>, NotFound,
            Conflict<PatchVerificationUploadFailureResponse>,
            UnprocessableEntity<PatchVerificationUploadFailureResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/patch-verification-targets/{runtimeInstanceId}/patch");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.PatchUpload)));
        Options(builder => builder.WithMetadata(
            new HumanVerificationMetadata(HumanVerificationAction.Evaluation)));
        AllowFileUploads();
        MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(
            PatchUploadRules.HardMaximumArchiveBytes));
        Description(builder => builder.ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
            StatusCodes.Status413PayloadTooLarge));
        Summary(summary =>
        {
            summary.Summary = "Uploads one Patch archive to a CTF PatchVerification target.";
            summary.Description = "The archive is validated before a FixAttempt fact is created.";
        });
    }

    public override async Task<Results<Accepted<UploadPatchResponse>, NotFound,
        Conflict<PatchVerificationUploadFailureResponse>,
        UnprocessableEntity<PatchVerificationUploadFailureResponse>,
        ProblemHttpResult>> ExecuteAsync(
        UploadPatchVerificationRequest request,
        CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.CompetitionChallengeId = Route<Guid>("competitionChallengeId");
        request.RuntimeInstanceId = Route<Guid>("runtimeInstanceId");
        await using var stream = request.File.OpenReadStream();
        var result = await upload.ExecuteAsync(
            request.CompetitionId,
            request.CompetitionChallengeId,
            request.RuntimeInstanceId,
            user.UserId,
            request.File.FileName,
            request.File.ContentType,
            stream,
            timeProvider.GetUtcNow(),
            ct,
            NoCTF.Domain.Runtime.RuntimePurpose.PatchVerificationTarget);
        if (result.FailureCode == PatchUploadFailureCode.PatchUploadNotAvailable)
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return MapFailure(result.FailureCode!.Value, result.ErrorMessage!);

        var statusUrl =
            $"/api/v1/competitions/{request.CompetitionId}/gameplay-facts/{result.Value!.GameplayFactId}";
        return TypedResults.Accepted(statusUrl, new UploadPatchResponse(
            result.Value.PatchUploadId,
            result.Value.GameplayFactId,
            GameplayFactMapper.ToProtocol(result.Value.State),
            statusUrl));
    }

    private static Results<Accepted<UploadPatchResponse>, NotFound,
        Conflict<PatchVerificationUploadFailureResponse>,
        UnprocessableEntity<PatchVerificationUploadFailureResponse>,
        ProblemHttpResult> MapFailure(PatchUploadFailureCode code, string detail) => code switch
        {
            PatchUploadFailureCode.ChallengeNotOpened or PatchUploadFailureCode.ChallengeSubmissionClosed => ApiProblems.Problem(
                statusCode: 409, detail: ApiMessages.For(code), extensions: new Dictionary<string, object?> { ["code"] = code }),
            PatchUploadFailureCode.DefenseTargetNotReady => TypedResults.Conflict(
                new PatchVerificationUploadFailureResponse(
                    PatchVerificationUploadFailureCode.TargetNotReady, detail)),
            PatchUploadFailureCode.DefenseAlreadySucceeded => TypedResults.Conflict(
                new PatchVerificationUploadFailureResponse(
                    PatchVerificationUploadFailureCode.PatchAlreadyVerified, detail)),
            PatchUploadFailureCode.AttemptsExhausted => TypedResults.Conflict(
                new PatchVerificationUploadFailureResponse(
                    PatchVerificationUploadFailureCode.AttemptsExhausted, detail)),
            PatchUploadFailureCode.DefenseTargetConsumed => TypedResults.Conflict(
                new PatchVerificationUploadFailureResponse(
                    PatchVerificationUploadFailureCode.TargetConsumed, detail)),
            PatchUploadFailureCode.PatchUploadConflict => TypedResults.Conflict(
                new PatchVerificationUploadFailureResponse(
                    PatchVerificationUploadFailureCode.UploadConflict, detail)),
            PatchUploadFailureCode.ArchiveStreamNotSeekable => TypedResults.UnprocessableEntity(
                new PatchVerificationUploadFailureResponse(
                    PatchVerificationUploadFailureCode.ArchiveStreamNotSeekable, detail)),
            PatchUploadFailureCode.ArchiveInvalid => TypedResults.UnprocessableEntity(
                new PatchVerificationUploadFailureResponse(
                    PatchVerificationUploadFailureCode.ArchiveInvalid, detail)),
            PatchUploadFailureCode.ArchiveTooLarge => ApiProblems.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: ApiMessages.Get(ApiMessageId.UploadPatchVerificationTitlePatchArchiveTooLarge),
                detail: ApiMessages.For(code),
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = PatchVerificationUploadFailureCode.ArchiveTooLarge.ToString()
                }),
            _ => ApiProblems.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: ApiMessages.Get(ApiMessageId.UploadPatchVerificationTitlePatchArchiveWasRejected),
                detail: ApiMessages.For(code),
                extensions: new Dictionary<string, object?> { ["code"] = code.ToString() })
        };
}

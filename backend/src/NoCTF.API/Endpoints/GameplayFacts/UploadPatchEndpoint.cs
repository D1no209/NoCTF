using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.Storage;
using NoCTF.API.Serialization;
using NoCTF.Application.Admission;

namespace NoCTF.API.Endpoints.GameplayFacts;

public sealed class UploadPatchRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid RuntimeInstanceId { get; set; }
    public IFormFile File { get; set; } = null!;
}

public sealed record UploadPatchResponse(
    Guid PatchUploadId,
    Guid GameplayFactId,
    GameplayFactStateProtocol State,
    string StatusUrl);

[JsonConverter(typeof(StrictPascalCaseEnumConverter<UploadPatchFailureCodeProtocol>))]
public enum UploadPatchFailureCodeProtocol
{
    ArchiveStreamNotSeekable,
    ArchiveInvalid,
    DefenseTargetNotReady,
    DefenseAlreadySucceeded,
    FixAttemptsExhausted,
    DefenseTargetConsumed
}

public sealed record UploadPatchFailureResponse(
    UploadPatchFailureCodeProtocol Code,
    string Detail);

public sealed class UploadPatchValidator : Validator<UploadPatchRequest>
{
    public UploadPatchValidator()
    {
        RuleFor(request => request.RuntimeInstanceId).NotEmpty();
        RuleFor(request => request.File).NotNull();
    }
}

public sealed class UploadPatchEndpoint(
    CreatePatchUpload upload,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<UploadPatchRequest,
        Results<Accepted<UploadPatchResponse>, NotFound,
            Conflict<UploadPatchFailureResponse>,
            UnprocessableEntity<UploadPatchFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Upload)));
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets/{runtimeInstanceId}/fix");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.PatchUpload)));
        Options(builder => builder.WithMetadata(
            new HumanVerificationMetadata(HumanVerificationAction.Evaluation)));
        AllowFileUploads();
        MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(
            PatchUploadRules.HardMaximumArchiveBytes));
        Description(builder => builder
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status413PayloadTooLarge));
        Summary(summary =>
        {
            summary.Summary = "Upload the only Fix archive accepted by an AWDP defense target.";
            summary.Description =
                "Atomically binds one archive and one Fix attempt to the clean disposable target. Verification starts immediately when the target is running, or automatically after provisioning completes.";
        });
    }

    public override async Task<
        Results<Accepted<UploadPatchResponse>, NotFound,
            Conflict<UploadPatchFailureResponse>,
            UnprocessableEntity<UploadPatchFailureResponse>, ProblemHttpResult>> ExecuteAsync(
        UploadPatchRequest request,
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
            ct);
        if (result.FailureCode == PatchUploadFailureCode.PatchUploadNotAvailable)
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            if (result.FailureCode is PatchUploadFailureCode.DefenseTargetNotReady)
            {
                return TypedResults.Conflict(new UploadPatchFailureResponse(
                    UploadPatchFailureCodeProtocol.DefenseTargetNotReady,
                    result.ErrorMessage!));
            }
            if (result.FailureCode is PatchUploadFailureCode.DefenseAlreadySucceeded)
            {
                return TypedResults.Conflict(new UploadPatchFailureResponse(
                    UploadPatchFailureCodeProtocol.DefenseAlreadySucceeded,
                    result.ErrorMessage!));
            }
            if (result.FailureCode is PatchUploadFailureCode.AttemptsExhausted)
            {
                return TypedResults.Conflict(new UploadPatchFailureResponse(
                    UploadPatchFailureCodeProtocol.FixAttemptsExhausted,
                    result.ErrorMessage!));
            }
            if (result.FailureCode is PatchUploadFailureCode.PatchUploadConflict)
            {
                return TypedResults.Conflict(new UploadPatchFailureResponse(
                    UploadPatchFailureCodeProtocol.DefenseTargetConsumed,
                    result.ErrorMessage!));
            }
            if (result.FailureCode is PatchUploadFailureCode.DefenseTargetConsumed)
            {
                return TypedResults.Conflict(new UploadPatchFailureResponse(
                    UploadPatchFailureCodeProtocol.DefenseTargetConsumed,
                    result.ErrorMessage!));
            }
            if (result.FailureCode is PatchUploadFailureCode.ArchiveStreamNotSeekable)
            {
                return TypedResults.UnprocessableEntity(new UploadPatchFailureResponse(
                    UploadPatchFailureCodeProtocol.ArchiveStreamNotSeekable,
                    result.ErrorMessage!));
            }
            if (result.FailureCode is PatchUploadFailureCode.ArchiveInvalid)
            {
                return TypedResults.UnprocessableEntity(new UploadPatchFailureResponse(
                    UploadPatchFailureCodeProtocol.ArchiveInvalid,
                    result.ErrorMessage!));
            }
            return TypedResults.Problem(
                statusCode: result.FailureCode == PatchUploadFailureCode.ArchiveTooLarge
                    ? StatusCodes.Status413PayloadTooLarge
                    : StatusCodes.Status422UnprocessableEntity,
                title: result.FailureCode == PatchUploadFailureCode.ArchiveTooLarge
                    ? "Patch archive is too large."
                    : "Patch archive was rejected.",
                detail: result.ErrorMessage,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = result.FailureCode?.ToString()
                });
        }
        var statusUrl =
            $"/api/v1/competitions/{request.CompetitionId}/gameplay-facts/{result.Value!.GameplayFactId}";
        return TypedResults.Accepted(
            statusUrl,
            new UploadPatchResponse(
                result.Value.PatchUploadId,
                result.Value.GameplayFactId,
                GameplayFactMapper.ToProtocol(result.Value.State),
                statusUrl));
    }
}

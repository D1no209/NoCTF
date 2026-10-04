using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Common;
using NoCTF.Application.Storage;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class UploadChallengeAttachmentsRequest
{
    public AttachmentDeliveryPolicyProtocol? DeliveryPolicy { get; set; }
    public string? DownloadFileName { get; set; }
    public IReadOnlyList<Guid>? AttachmentIds { get; set; }
    public IReadOnlyList<IFormFile> Files { get; set; } = [];
}

public sealed class UploadChallengeAttachmentsValidator
    : Validator<UploadChallengeAttachmentsRequest>
{
    public UploadChallengeAttachmentsValidator()
    {
        RuleFor(request => request.DeliveryPolicy).NotNull().IsInEnum();
        RuleFor(request => request.AttachmentIds)
            .Must((request, ids) => ids is null || ids.Count == 0 || ids.Count == request.Files.Count)
            .WithMessage(_ => ApiMessages.Text(ApiMessageId.UploadChallengeAttachmentsValidationAttachmentidsEmptyContainOne)).WithErrorCode(ApiMessages.Key(ApiMessageId.UploadChallengeAttachmentsValidationAttachmentidsEmptyContainOne))
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage(_ => ApiMessages.Text(ApiMessageId.UploadChallengeAttachmentsValidationAttachmentidsContainDuplicates)).WithErrorCode(ApiMessages.Key(ApiMessageId.UploadChallengeAttachmentsValidationAttachmentidsContainDuplicates));
        RuleForEach(request => request.AttachmentIds!).NotEmpty()
            .When(request => request.AttachmentIds is not null);
        RuleFor(request => request.Files).NotEmpty().Must(files => files.Count <= 128);
        RuleFor(request => request.DownloadFileName).NotEmpty().MaximumLength(260)
            .When(request => request.DeliveryPolicy
                == AttachmentDeliveryPolicyProtocol.RandomOnePerTeam);
    }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AttachmentBatchFailureCodeProtocol>))]
public enum AttachmentBatchFailureCodeProtocol
{
    UploadTooLarge,
    InvalidFileName,
    InvalidVariantFileName,
    EmptyBatch,
    DuplicateFlag,
    DeliveryModeConflict,
    BatchStorageFailed,
    BatchPersistenceFailed,
    ResourceIdConflict
}

public sealed record AttachmentBatchFailureResponse(
    AttachmentBatchFailureCodeProtocol Code,
    string Message)
{
    public string Message { get; init; } = ApiMessages.Localize(Code, Message, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed class UploadChallengeAttachmentsEndpoint(
    ManageChallengeAttachments attachments,
    IUserContext user,
    FileUploadLimits uploadLimits,
    TimeProvider timeProvider)
    : Endpoint<UploadChallengeAttachmentsRequest,
        Results<Created<ChallengeAttachmentListResponse>, NotFound,
            Conflict<AttachmentBatchFailureResponse>,
            JsonHttpResult<AttachmentBatchFailureResponse>>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(
            new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
                NoCTF.Application.Observability.ApiRequestKind.Upload)));
        Post("/admin/challenges/{challengeId}/attachments");
        AuthSchemes("Bearer");
        AllowFileUploads();
        Description(builder => builder.Accepts<UploadChallengeAttachmentsRequest>("multipart/form-data"));
        Description(builder => builder.WithName("AdminChallengeBankUploadAttachments"));
        Summary(summary =>
        {
            summary.Summary = "Uploads an atomic challenge attachment batch.";
            summary.Description =
                "DeliveryPolicy selects ordinary attachments or RandomOnePerTeam exact-Flag variants.";
        });
    }

    public override async Task<Results<Created<ChallengeAttachmentListResponse>, NotFound,
        Conflict<AttachmentBatchFailureResponse>,
        JsonHttpResult<AttachmentBatchFailureResponse>>> ExecuteAsync(
        UploadChallengeAttachmentsRequest request,
        CancellationToken ct)
    {
        if (request.Files.Any(file => file.Length > uploadLimits.MaximumAttachmentBytes))
        {
            return TypedResults.Json(
                new AttachmentBatchFailureResponse(
                    AttachmentBatchFailureCodeProtocol.UploadTooLarge,
                    $"Each attachment cannot exceed {uploadLimits.MaximumAttachmentBytes} bytes."),
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var streams = request.Files.Select(file => file.OpenReadStream()).ToArray();
        try
        {
            OperationResult<ChallengeAttachmentSet, ChallengeAttachmentFailureCode> result;
            if (request.DeliveryPolicy == AttachmentDeliveryPolicyProtocol.RandomOnePerTeam)
            {
                result = await attachments.UploadRandomBatchAsync(
                    Route<Guid>("challengeId"),
                    user.UserId,
                    user.IsAdministrator,
                    request.DownloadFileName!,
                    request.Files.Select((file, index) => new RandomAttachmentUploadItem(
                        file.FileName,
                        file.ContentType,
                        streams[index],
                        RequestedAttachmentId(request, index))).ToArray(),
                    timeProvider.GetUtcNow(),
                    ct);
            }
            else
            {
                result = await attachments.UploadBatchAsync(
                    Route<Guid>("challengeId"),
                    user.UserId,
                    user.IsAdministrator,
                    request.Files.Select((file, index) => new ChallengeAttachmentUploadItem(
                        file.FileName,
                        file.ContentType,
                        streams[index],
                        RequestedAttachmentId(request, index))).ToArray(),
                    timeProvider.GetUtcNow(),
                    ct);
            }
            if (result.FailureCode == ChallengeAttachmentFailureCode.ChallengeNotFound)
                return TypedResults.NotFound();
            if (!result.Succeeded)
            {
                var failure = new AttachmentBatchFailureResponse(
                    MapFailure(result.FailureCode!.Value),
                    result.ErrorMessage ?? "Attachment batch was not uploaded.");
                if (result.FailureCode is ChallengeAttachmentFailureCode.BatchStorageFailed
                    or ChallengeAttachmentFailureCode.BatchPersistenceFailed)
                {
                    return TypedResults.Json(
                        failure,
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                return TypedResults.Conflict(failure);
            }
            var response = new ChallengeAttachmentListResponse(
                ChallengeAttachmentMapping.ToProtocol(result.Value!.DeliveryPolicy),
                result.Value.Items.Select(item =>
                    ChallengeAttachmentMapping.ToResponse(item)).ToArray());
            return TypedResults.Created(
                $"/api/v1/admin/challenges/{Route<Guid>("challengeId")}/attachments",
                response);
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

    private static Guid? RequestedAttachmentId(
        UploadChallengeAttachmentsRequest request,
        int index) =>
        request.AttachmentIds is not { Count: > 0 } ? null : request.AttachmentIds[index];

    private static AttachmentBatchFailureCodeProtocol MapFailure(
        ChallengeAttachmentFailureCode code) => code switch
        {
            ChallengeAttachmentFailureCode.InvalidFileName => AttachmentBatchFailureCodeProtocol.InvalidFileName,
            ChallengeAttachmentFailureCode.InvalidVariantFileName => AttachmentBatchFailureCodeProtocol.InvalidVariantFileName,
            ChallengeAttachmentFailureCode.EmptyBatch => AttachmentBatchFailureCodeProtocol.EmptyBatch,
            ChallengeAttachmentFailureCode.DuplicateFlag => AttachmentBatchFailureCodeProtocol.DuplicateFlag,
            ChallengeAttachmentFailureCode.DeliveryModeConflict => AttachmentBatchFailureCodeProtocol.DeliveryModeConflict,
            ChallengeAttachmentFailureCode.BatchStorageFailed => AttachmentBatchFailureCodeProtocol.BatchStorageFailed,
            ChallengeAttachmentFailureCode.BatchPersistenceFailed => AttachmentBatchFailureCodeProtocol.BatchPersistenceFailed,
            ChallengeAttachmentFailureCode.ResourceIdConflict => AttachmentBatchFailureCodeProtocol.ResourceIdConflict,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
        };
}

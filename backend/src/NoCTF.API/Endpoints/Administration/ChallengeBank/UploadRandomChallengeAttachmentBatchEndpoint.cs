using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Storage;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class UploadRandomChallengeAttachmentBatchRequest
{
    public string DownloadFileName { get; set; } = string.Empty;
    public IReadOnlyList<IFormFile> Files { get; set; } = [];
}

public sealed class UploadRandomChallengeAttachmentBatchValidator
    : Validator<UploadRandomChallengeAttachmentBatchRequest>
{
    public UploadRandomChallengeAttachmentBatchValidator()
    {
        RuleFor(request => request.DownloadFileName).NotEmpty().MaximumLength(260);
        RuleFor(request => request.Files).NotEmpty();
    }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RandomAttachmentBatchFailureCodeProtocol>))]
public enum RandomAttachmentBatchFailureCodeProtocol
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

public sealed record RandomAttachmentBatchFailureResponse(
    RandomAttachmentBatchFailureCodeProtocol Code,
    string Message);

public sealed class UploadRandomChallengeAttachmentBatchEndpoint(
    ManageChallengeAttachments attachments,
    IUserContext user,
    FileUploadLimits uploadLimits,
    TimeProvider timeProvider)
    : Endpoint<UploadRandomChallengeAttachmentBatchRequest,
        Results<
            Created<ChallengeAttachmentListResponse>,
            NotFound,
            Conflict<RandomAttachmentBatchFailureResponse>,
            JsonHttpResult<RandomAttachmentBatchFailureResponse>>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/attachments/random-batch");
        AuthSchemes("Bearer");
        AllowFileUploads();
        Description(builder => builder.WithName("AdminChallengeBankUploadRandomAttachmentBatch"));
        Summary(summary =>
        {
            summary.Summary = "Uploads one atomic RandomOnePerTeam attachment batch.";
            summary.Description =
                "Each original multipart file name is used verbatim as an exact protected Flag, while every stored file uses the common player-facing download name.";
        });
    }

    public override async Task<Results<
        Created<ChallengeAttachmentListResponse>,
        NotFound,
        Conflict<RandomAttachmentBatchFailureResponse>,
        JsonHttpResult<RandomAttachmentBatchFailureResponse>>>
        ExecuteAsync(
            UploadRandomChallengeAttachmentBatchRequest request,
            CancellationToken ct)
    {
        if (request.Files.Any(file => file.Length > uploadLimits.MaximumAttachmentBytes))
        {
            return TypedResults.Json(
                new RandomAttachmentBatchFailureResponse(
                    RandomAttachmentBatchFailureCodeProtocol.UploadTooLarge,
                    $"Each attachment variant cannot exceed {uploadLimits.MaximumAttachmentBytes} bytes."),
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var streams = request.Files.Select(file => file.OpenReadStream()).ToArray();
        try
        {
            var result = await attachments.UploadRandomBatchAsync(
                Route<Guid>("challengeId"),
                user.UserId,
                user.IsAdministrator,
                request.DownloadFileName,
                request.Files.Select((file, index) => new RandomAttachmentUploadItem(
                    file.FileName,
                    file.ContentType,
                    streams[index])).ToArray(),
                timeProvider.GetUtcNow(),
                ct);
            if (result.FailureCode == ChallengeAttachmentFailureCode.ChallengeNotFound)
                return TypedResults.NotFound();
            if (!result.Succeeded)
            {
                var failure = new RandomAttachmentBatchFailureResponse(
                    MapFailureCode(result.FailureCode!.Value),
                    result.ErrorMessage ?? "Random attachment batch was not uploaded.");
                if (result.FailureCode is ChallengeAttachmentFailureCode.BatchStorageFailed
                    or ChallengeAttachmentFailureCode.BatchPersistenceFailed)
                    return TypedResults.Json(failure, statusCode: StatusCodes.Status503ServiceUnavailable);
                return TypedResults.Conflict(failure);
            }

            var response = new ChallengeAttachmentListResponse(
                ChallengeAttachmentMapping.ToProtocol(result.Value!.DeliveryPolicy),
                result.Value.Items.Select(item => ChallengeAttachmentMapping.ToResponse(item)).ToArray());
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

    private static RandomAttachmentBatchFailureCodeProtocol MapFailureCode(
        ChallengeAttachmentFailureCode code) =>
        code switch
        {
            ChallengeAttachmentFailureCode.InvalidFileName => RandomAttachmentBatchFailureCodeProtocol.InvalidFileName,
            ChallengeAttachmentFailureCode.InvalidVariantFileName => RandomAttachmentBatchFailureCodeProtocol.InvalidVariantFileName,
            ChallengeAttachmentFailureCode.EmptyBatch => RandomAttachmentBatchFailureCodeProtocol.EmptyBatch,
            ChallengeAttachmentFailureCode.DuplicateFlag => RandomAttachmentBatchFailureCodeProtocol.DuplicateFlag,
            ChallengeAttachmentFailureCode.DeliveryModeConflict => RandomAttachmentBatchFailureCodeProtocol.DeliveryModeConflict,
            ChallengeAttachmentFailureCode.BatchStorageFailed => RandomAttachmentBatchFailureCodeProtocol.BatchStorageFailed,
            ChallengeAttachmentFailureCode.BatchPersistenceFailed => RandomAttachmentBatchFailureCodeProtocol.BatchPersistenceFailed,
            ChallengeAttachmentFailureCode.ResourceIdConflict => RandomAttachmentBatchFailureCodeProtocol.ResourceIdConflict,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
        };
}

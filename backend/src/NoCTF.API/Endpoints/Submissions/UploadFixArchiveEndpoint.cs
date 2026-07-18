using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Submissions;

public sealed class UploadFixArchiveRequest
{
    public Guid UploadId { get; set; }
    public IFormFile File { get; set; } = default!;
}

public sealed class UploadFixArchiveEndpoint(
    IFixUploadSessionStore sessions,
    IObjectStorage storage,
    IUserContext userContext) : Endpoint<UploadFixArchiveRequest,
        Results<Ok<StoredObject>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/storage/uploads/{uploadId}");
        AuthSchemes("Bearer");
        AllowFileUploads();
        Description(builder => builder
            .ProducesProblemFE(StatusCodes.Status400BadRequest)
            .ProducesProblemFE(StatusCodes.Status403Forbidden)
            .ProducesProblemFE(StatusCodes.Status404NotFound));
    }

    public override async Task<Results<Ok<StoredObject>, NotFound, ProblemHttpResult>> ExecuteAsync(
        UploadFixArchiveRequest request,
        CancellationToken cancellationToken)
    {
        request.UploadId = Route<Guid>("uploadId");
        if (request.File is null || request.File.Length <= 0)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Fix archive is required.",
                detail: "Upload a non-empty multipart file using the 'file' field.");
        }

        var metadata = await sessions.GetAuthorizedMetadataAsync(
            request.UploadId,
            Guid.Empty,
            Guid.Empty,
            Guid.Empty,
            userContext.UserId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (metadata is null)
        {
            return TypedResults.NotFound();
        }
        if (request.File.Length != metadata.Length)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Fix archive length mismatch.",
                detail: "The uploaded archive length does not match the upload grant.");
        }

        await using var content = request.File.OpenReadStream();
        var stored = await storage.PutAsync(metadata.ObjectKey, metadata.FileName, metadata.ContentType,
            content, cancellationToken);
        if (!string.Equals(stored.Sha256, metadata.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            await storage.DeleteAsync(metadata.ObjectKey, cancellationToken);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Fix archive checksum mismatch.",
                detail: "The uploaded archive checksum does not match the upload grant.");
        }
        return TypedResults.Ok(stored);
    }
}

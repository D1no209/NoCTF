using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Internal;

public sealed class DownloadFixArchiveRequest
{
    public Guid UploadId { get; set; }
}

public sealed class DownloadFixArchiveEndpoint(
    IFixArchiveDownloadStore archives,
    IObjectStorage storage)
    : Endpoint<DownloadFixArchiveRequest, Results<FileStreamHttpResult, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/internal/fix-archives/{uploadId}");
        AuthSchemes("RunnerScoringBearer");
        Policies("FixArchiveRead");
        Summary(summary => summary.Summary = "Download the single Fix archive bound to a Runner JWT.");
    }

    public override async Task<Results<FileStreamHttpResult, NotFound, ProblemHttpResult>> ExecuteAsync(
        DownloadFixArchiveRequest request,
        CancellationToken cancellationToken)
    {
        request.UploadId = Route<Guid>("uploadId");
        if (!Guid.TryParse(User.FindFirst("upload_id")?.Value, out var tokenUploadId)
            || !Guid.TryParse(User.FindFirst("submission_id")?.Value, out var submissionId)
            || tokenUploadId != request.UploadId)
            return TypedResults.NotFound();

        var archive = await archives.AuthorizeAsync(
            request.UploadId, submissionId, DateTimeOffset.UtcNow, cancellationToken);
        if (archive is null) return TypedResults.NotFound();
        try
        {
            var stream = await storage.OpenReadAsync(archive.ObjectKey, cancellationToken);
            return TypedResults.File(stream, archive.ContentType, archive.FileName, enableRangeProcessing: false);
        }
        catch (FileNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Fix archive storage is unavailable.");
        }
    }
}

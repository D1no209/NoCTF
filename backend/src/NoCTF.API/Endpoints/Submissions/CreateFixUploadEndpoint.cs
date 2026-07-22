using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Submissions;

public sealed class CreateFixUploadRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed record CreateFixUploadResponse(Guid UploadId, string ObjectKey, Uri UploadUrl, DateTimeOffset ExpiresAt);

public sealed class CreateFixUploadEndpoint(
    CreateFixUpload createUpload,
    IUserContext userContext) : Endpoint<CreateFixUploadRequest,
        Results<Created<CreateFixUploadResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/submissions/fixes/uploads");
        AuthSchemes("Bearer");
        Options(options => options.WithMetadata(new EnableRateLimitingAttribute("submission")));
        Description(builder => builder.ProducesProblemFE(StatusCodes.Status400BadRequest)
            .ProducesProblemFE(StatusCodes.Status403Forbidden)
            .ProducesProblemFE(StatusCodes.Status409Conflict));
    }

    public override async Task<Results<Created<CreateFixUploadResponse>, ProblemHttpResult>> ExecuteAsync(
        CreateFixUploadRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await createUpload.ExecuteAsync(new(
            request.CompetitionId,
            request.TeamId,
            request.CompetitionChallengeId,
            userContext.UserId,
            request.FileName,
            request.ContentType,
            request.Length,
            request.Sha256,
            DateTimeOffset.UtcNow), cancellationToken);
        if (!result.Succeeded)
        {
            return SubmissionProblemDetails.Create(SubmissionProblemDetails.StatusFor(result.ErrorCode), result.ErrorCode, result.ErrorMessage);
        }
        var grant = result.Value!.Grant;
        return TypedResults.Created($"/storage/uploads/{grant.UploadId}",
            new CreateFixUploadResponse(grant.UploadId, grant.ObjectKey, grant.UploadUrl, grant.ExpiresAt));
    }
}

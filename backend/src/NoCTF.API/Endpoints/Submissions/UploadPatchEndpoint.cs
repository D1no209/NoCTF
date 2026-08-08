using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.PatchUploads;

namespace NoCTF.API.Endpoints.Submissions;

public sealed class UploadPatchRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public IFormFile File { get; set; } = null!;
}

public sealed record UploadPatchResponse(Guid PatchUploadId);

public sealed class UploadPatchValidator : Validator<UploadPatchRequest>
{
    public UploadPatchValidator() =>
        RuleFor(request => request.File).NotNull();
}

public sealed class UploadPatchEndpoint(
    CreatePatchUpload upload,
    IUserContext user)
    : Endpoint<UploadPatchRequest,
        Results<Created<UploadPatchResponse>, NotFound, UnprocessableEntity<Microsoft.AspNetCore.Mvc.ProblemDetails>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/patch-upload");
        AuthSchemes("Bearer");
        AllowFileUploads();
        Summary(summary =>
        {
            summary.Summary = "Upload an AWDP patch archive";
            summary.Description = "Validates and stores one gzip-compressed tar archive without creating a submission.";
        });
    }

    public override async Task<
        Results<Created<UploadPatchResponse>, NotFound,
            UnprocessableEntity<Microsoft.AspNetCore.Mvc.ProblemDetails>>> ExecuteAsync(
        UploadPatchRequest request,
        CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.CompetitionChallengeId = Route<Guid>("competitionChallengeId");
        await using var stream = request.File.OpenReadStream();
        var result = await upload.ExecuteAsync(
            request.CompetitionId,
            request.CompetitionChallengeId,
            user.UserId,
            request.File.FileName,
            request.File.ContentType,
            stream,
            DateTimeOffset.UtcNow,
            ct);
        if (result.FailureCode == PatchUploadFailureCode.PatchUploadNotAvailable)
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.UnprocessableEntity(new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Patch archive was rejected.",
                Detail = result.ErrorMessage,
                Extensions = { ["code"] = result.FailureCode?.ToString() }
            });
        return TypedResults.Created(
            $"/api/v1/competitions/{request.CompetitionId}/challenges/{request.CompetitionChallengeId}/patch-upload",
            new UploadPatchResponse(result.Value!.PatchUploadId));
    }
}

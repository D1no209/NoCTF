using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class UploadChallengeAttachmentRequest
{
    public Guid? Id { get; set; }
    public IFormFile File { get; set; } = null!;
}

public sealed class UploadChallengeAttachmentValidator : Validator<UploadChallengeAttachmentRequest>
{
    public UploadChallengeAttachmentValidator()
    {
        RuleFor(request => request.File).NotNull();
        RuleFor(request => request.Id)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("Id cannot be empty when supplied.");
    }
}

public sealed class UploadChallengeAttachmentEndpoint(
    ManageChallengeAttachments attachments,
    IUserContext user)
    : Endpoint<UploadChallengeAttachmentRequest, Results<Created<ChallengeAttachmentResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/attachments");
        AuthSchemes("Bearer");
        AllowFileUploads();
        Description(builder => builder.WithName("AdminChallengeBankUploadAttachment"));
        Summary(summary =>
        {
            summary.Summary = "Uploads a challenge template attachment.";
            summary.Description = "The server generates the object key and persists length and SHA-256 metadata.";
        });
    }

    public override async Task<Results<Created<ChallengeAttachmentResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        UploadChallengeAttachmentRequest request,
        CancellationToken ct)
    {
        await using var content = request.File.OpenReadStream();
        var result = await attachments.UploadAsync(
            Route<Guid>("challengeId"),
            user.UserId,
            user.IsAdministrator,
            request.Id,
            request.File.FileName,
            request.File.ContentType,
            content,
            DateTimeOffset.UtcNow,
            ct);
        if (result.FailureCode == ChallengeAttachmentFailureCode.ChallengeNotFound)
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: result.FailureCode == ChallengeAttachmentFailureCode.ResourceIdConflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest,
                title: "Attachment was not uploaded.",
                detail: result.ErrorMessage,
                extensions: new Dictionary<string, object?> { ["code"] = result.FailureCode?.ToString() });
        var response = ChallengeAttachmentMapping.ToResponse(result.Value!);
        return TypedResults.Created(
            $"/api/v1/admin/challenges/{response.ChallengeId}/attachments/{response.Id}",
            response);
    }
}

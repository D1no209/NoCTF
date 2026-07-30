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
            .NotEqual(Guid.Empty)
            .When(request => request.Id is not null);
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
            request.File.FileName,
            request.File.ContentType,
            content,
            request.Id,
            DateTimeOffset.UtcNow,
            ct);
        if (result.ErrorCode == "challenge_not_found")
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Attachment was not uploaded.",
                detail: result.ErrorMessage);
        var response = ChallengeAttachmentMapping.ToResponse(result.Value!);
        return TypedResults.Created(
            $"/api/v1/admin/challenges/{response.ChallengeId}/attachments/{response.Id}",
            response);
    }
}

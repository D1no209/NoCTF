using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class UpdateChallengeAttachmentRequest
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
}

public sealed class UpdateChallengeAttachmentValidator : Validator<UpdateChallengeAttachmentRequest>
{
    public UpdateChallengeAttachmentValidator()
    {
        RuleFor(request => request.FileName).NotEmpty().MaximumLength(260);
        RuleFor(request => request.ContentType).NotEmpty().MaximumLength(255);
    }
}

public sealed class UpdateChallengeAttachmentEndpoint(
    ManageChallengeAttachments attachments,
    IUserContext user)
    : Endpoint<UpdateChallengeAttachmentRequest, Results<Ok<ChallengeAttachmentResponse>, NotFound>>
{
    public override void Configure()
    {
        Put("/admin/challenges/{challengeId}/attachments/{attachmentId}");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Updates attachment display metadata.");
    }

    public override async Task<Results<Ok<ChallengeAttachmentResponse>, NotFound>> ExecuteAsync(
        UpdateChallengeAttachmentRequest request,
        CancellationToken ct)
    {
        var result = await attachments.UpdateAsync(
            Route<Guid>("challengeId"),
            Route<Guid>("attachmentId"),
            user.UserId,
            user.IsAdministrator,
            request.FileName,
            request.ContentType,
            ct);
        return result.Succeeded
            ? TypedResults.Ok(ChallengeAttachmentMapping.ToResponse(result.Value!))
            : TypedResults.NotFound();
    }
}

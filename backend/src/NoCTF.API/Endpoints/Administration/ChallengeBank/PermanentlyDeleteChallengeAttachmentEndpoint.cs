using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class PermanentlyDeleteChallengeAttachmentRequest
{
    public Guid ChallengeId { get; set; }
    public Guid AttachmentId { get; set; }
}
public sealed class PermanentlyDeleteChallengeAttachmentValidator : Validator<PermanentlyDeleteChallengeAttachmentRequest>
{
    public PermanentlyDeleteChallengeAttachmentValidator()
    {
        RuleFor(x => x.ChallengeId).NotEmpty();
        RuleFor(x => x.AttachmentId).NotEmpty();
    }
}
public sealed class PermanentlyDeleteChallengeAttachmentEndpoint(ManageChallengeAttachments attachments, IUserContext user, TimeProvider clock)
    : Endpoint<PermanentlyDeleteChallengeAttachmentRequest, Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Delete("/admin/challenges/{challengeId}/attachments/{attachmentId}/permanent");
        AuthSchemes("Bearer");
        Description(x => x.WithName("AdminChallengeBankPermanentlyDeleteAttachment"));
    }
    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(PermanentlyDeleteChallengeAttachmentRequest req, CancellationToken ct) =>
        await attachments.PermanentlyDeleteAsync(req.ChallengeId, req.AttachmentId, user.UserId, user.IsAdministrator, clock.GetUtcNow(), ct)
            ? TypedResults.NoContent() : TypedResults.NotFound();
}

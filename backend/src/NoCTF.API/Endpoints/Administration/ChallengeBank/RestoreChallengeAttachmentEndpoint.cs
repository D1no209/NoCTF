using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class RestoreChallengeAttachmentEndpoint(
    ManageChallengeAttachments attachments,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/attachments/{attachmentId}/restore");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankRestoreAttachment"));
        Summary(summary =>
        {
            summary.Summary = "Restores a soft-deleted challenge attachment.";
            summary.Description =
                "Restores immutable attachment metadata and its retained object reference.";
        });
    }

    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await attachments.RestoreAsync(
            Route<Guid>("challengeId"),
            Route<Guid>("attachmentId"),
            user.UserId,
            user.IsAdministrator,
            DateTimeOffset.UtcNow,
            ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}

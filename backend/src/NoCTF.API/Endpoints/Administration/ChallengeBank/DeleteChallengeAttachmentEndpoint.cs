using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class DeleteChallengeAttachmentEndpoint(
    ManageChallengeAttachments attachments,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Delete("/admin/challenges/{challengeId}/attachments/{attachmentId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankDeleteAttachment"));
        Summary(summary =>
        {
            summary.Summary = "Deletes a challenge attachment.";
            summary.Description = "Soft-deletes attachment metadata and schedules owned object cleanup.";
        });
    }

    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var result = await attachments.DeleteAsync(
            Route<Guid>("challengeId"),
            Route<Guid>("attachmentId"),
            user.UserId,
            user.IsAdministrator,
            DateTimeOffset.UtcNow,
            ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}

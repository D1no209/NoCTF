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
        Summary(summary => summary.Summary = "Soft-deletes a challenge attachment.");
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

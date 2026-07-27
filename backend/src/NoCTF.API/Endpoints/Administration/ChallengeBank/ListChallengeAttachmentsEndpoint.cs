using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed record ChallengeAttachmentResponse(
    Guid Id,
    Guid ChallengeId,
    string FileName,
    string ContentType,
    long ByteLength,
    string Sha256,
    DateTimeOffset CreatedAt);

public sealed record ChallengeAttachmentListResponse(IReadOnlyList<ChallengeAttachmentResponse> Items);

internal static class ChallengeAttachmentMapping
{
    public static ChallengeAttachmentResponse ToResponse(ChallengeAttachmentView view) =>
        new(view.Id, view.ChallengeId, view.FileName, view.ContentType, view.ByteLength, view.Sha256, view.CreatedAt);
}

public sealed class ListChallengeAttachmentsEndpoint(
    ManageChallengeAttachments attachments,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<ChallengeAttachmentListResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/challenges/{challengeId}/attachments");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankListAttachments"));
        Summary(summary =>
        {
            summary.Summary = "Lists global challenge template attachments.";
            summary.Description = "Returns protected attachment metadata to authorized template managers.";
        });
    }

    public override async Task<Results<Ok<ChallengeAttachmentListResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var items = await attachments.ListAsync(
            Route<Guid>("challengeId"), user.UserId, user.IsAdministrator, ct);
        return items is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ChallengeAttachmentListResponse(
                items.Select(ChallengeAttachmentMapping.ToResponse).ToArray()));
    }
}

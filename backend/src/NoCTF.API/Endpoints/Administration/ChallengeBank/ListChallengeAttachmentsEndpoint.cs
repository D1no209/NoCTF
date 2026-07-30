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
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt);

public sealed record ChallengeAttachmentListResponse(IReadOnlyList<ChallengeAttachmentResponse> Items);

internal static class ChallengeAttachmentMapping
{
    public static ChallengeAttachmentResponse ToResponse(ChallengeAttachmentView view) =>
        new(
            view.Id,
            view.ChallengeId,
            view.FileName,
            view.ContentType,
            view.ByteLength,
            view.Sha256,
            view.DeletedAt,
            view.CreatedAt);
}

public sealed class ListChallengeAttachmentsRequest
{
    public Guid ChallengeId { get; set; }
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}

public sealed class ListChallengeAttachmentsEndpoint(
    ManageChallengeAttachments attachments,
    IUserContext user)
    : Endpoint<ListChallengeAttachmentsRequest,
        Results<Ok<ChallengeAttachmentListResponse>, NotFound>>
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
        ListChallengeAttachmentsRequest request,
        CancellationToken ct)
    {
        var items = await attachments.ListAsync(
            request.ChallengeId,
            user.UserId,
            user.IsAdministrator,
            request.IncludeDeleted,
            ct);
        return items is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ChallengeAttachmentListResponse(
                items.Select(ChallengeAttachmentMapping.ToResponse).ToArray()));
    }
}

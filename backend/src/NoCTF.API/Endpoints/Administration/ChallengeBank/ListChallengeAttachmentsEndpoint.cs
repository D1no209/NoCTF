using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json.Serialization;
using NoCTF.API.Serialization;
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
    string? ExactFlag,
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt);

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AttachmentDeliveryPolicyProtocol>))]
public enum AttachmentDeliveryPolicyProtocol
{
    All,
    RandomOnePerTeam
}

public sealed record ChallengeAttachmentListResponse(
    AttachmentDeliveryPolicyProtocol DeliveryPolicy,
    IReadOnlyList<ChallengeAttachmentResponse> Items);

internal static class ChallengeAttachmentMapping
{
    public static ChallengeAttachmentResponse ToResponse(
        ChallengeAttachmentView view,
        bool includeProtectedFlag = true) =>
        new(
            view.Id,
            view.ChallengeId,
            view.FileName,
            view.ContentType,
            view.ByteLength,
            view.Sha256,
            includeProtectedFlag ? view.ExactFlag : null,
            view.DeletedAt,
            view.CreatedAt);

    public static AttachmentDeliveryPolicyProtocol ToProtocol(AttachmentDeliveryPolicy policy) =>
        policy switch
        {
            AttachmentDeliveryPolicy.All => AttachmentDeliveryPolicyProtocol.All,
            AttachmentDeliveryPolicy.RandomOnePerTeam =>
                AttachmentDeliveryPolicyProtocol.RandomOnePerTeam,
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null)
        };
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
        var result = await attachments.ListAsync(
            request.ChallengeId,
            user.UserId,
            user.IsAdministrator,
            request.IncludeDeleted,
            ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ChallengeAttachmentListResponse(
                ChallengeAttachmentMapping.ToProtocol(result.DeliveryPolicy),
                result.Items.Select(item => ChallengeAttachmentMapping.ToResponse(item)).ToArray()));
    }
}

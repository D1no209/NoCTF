using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;

namespace NoCTF.API.Endpoints.Challenges;

public sealed class ListChallengeAttachmentsEndpoint(
    GetChallengeAttachments attachments,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<ChallengeAttachmentListResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Lists the player attachment delivery surface.";
            summary.Description = "All returns every attachment; RandomOnePerTeam returns one assigned-download entry without exposing variants.";
        });
    }

    public override async Task<Results<Ok<ChallengeAttachmentListResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var items = await attachments.ListAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            ct);
        return items is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ChallengeAttachmentListResponse(
                ChallengeAttachmentMapping.ToProtocol(items.DeliveryPolicy),
                items.Items.Select(item => ChallengeAttachmentMapping.ToResponse(
                    item,
                    includeProtectedFlag: false)).ToArray()));
    }
}

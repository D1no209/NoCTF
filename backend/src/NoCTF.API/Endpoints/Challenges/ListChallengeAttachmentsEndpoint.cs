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
            summary.Summary = "Lists All-policy challenge attachments.";
            summary.Description = "RandomOnePerTeam challenges deliberately return 404 from this route.";
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
                items.Select(ChallengeAttachmentMapping.ToResponse).ToArray()));
    }
}

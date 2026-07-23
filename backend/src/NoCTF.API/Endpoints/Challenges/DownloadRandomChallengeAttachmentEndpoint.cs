using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;

namespace NoCTF.API.Endpoints.Challenges;

public sealed class DownloadRandomChallengeAttachmentEndpoint(
    GetChallengeAttachments attachments,
    IUserContext user)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/attachment");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Downloads the team's fixed RandomOnePerTeam attachment.";
            summary.Description = "The first request atomically selects a candidate and persists the selection as a team ChallengeFlag fact.";
        });
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var result = await attachments.OpenAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            attachmentId: null,
            user.UserId,
            ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(result.Value.Content, result.Value.Metadata.ContentType, result.Value.Metadata.FileName);
    }
}

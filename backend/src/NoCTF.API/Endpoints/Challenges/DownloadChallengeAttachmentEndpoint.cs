using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;

namespace NoCTF.API.Endpoints.Challenges;

public sealed class DownloadChallengeAttachmentEndpoint(
    GetChallengeAttachments attachments,
    IUserContext user)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Download), new AttachmentBrowserDownloadMetadata()));
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments/{attachmentId}");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Downloads one All-policy challenge attachment.");
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var result = await attachments.OpenAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            Route<Guid>("attachmentId"),
            user.UserId,
            ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(result.Value.Content, result.Value.Metadata.ContentType, result.Value.Metadata.FileName);
    }
}

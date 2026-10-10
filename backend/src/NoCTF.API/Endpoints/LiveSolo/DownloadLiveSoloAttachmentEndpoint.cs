using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class DownloadLiveSoloAttachmentRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid AttachmentId { get; set; }
}
internal static class LiveSoloAttachmentTransport
{
    public static async Task<Results<FileStreamHttpResult, NotFound, ProblemHttpResult>> OpenAsync(AccessLiveSoloAttachments attachments,
        LiveSoloResourceRequest request, Guid? attachmentId, CancellationToken ct)
    {
        try
        {
            var result = await attachments.OpenAsync(request, attachmentId, ct);
            return result.Failure switch
            {
                null when result.Content is not null && result.Metadata is not null => TypedResults.Stream(result.Content, result.Metadata.ContentType, result.Metadata.FileName),
                LiveSoloFailure.DependencyUnavailable => LiveSoloProtocol.Unavailable(result.Failure.Value),
                _ => TypedResults.NotFound()
            };
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TimeoutException)
        { return LiveSoloProtocol.Unavailable(LiveSoloFailure.DependencyUnavailable); }
    }
}
public sealed class DownloadLiveSoloAttachmentEndpoint(AccessLiveSoloAttachments attachments, IUserContext user, TimeProvider clock)
    : Endpoint<DownloadLiveSoloAttachmentRequest, Results<FileStreamHttpResult, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/attachments/{attachmentId}"); AuthSchemes("Bearer");
        Options(x => x.WithMetadata(new AttachmentBrowserDownloadMetadata(), new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(NoCTF.Application.Observability.ApiRequestKind.Download)));
        Description(x => x.WithName("DownloadLiveSoloAttachment"));
        Summary(x => x.Summary = "Streams a scoped attachment to the browser and records evidence only after the object stream opens and authorization is rechecked.");
    }
    public override Task<Results<FileStreamHttpResult, NotFound, ProblemHttpResult>> ExecuteAsync(DownloadLiveSoloAttachmentRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        return LiveSoloAttachmentTransport.OpenAsync(attachments, new(req.CompetitionId, req.MatchId, req.RoundId, req.QuestionId,
            user.UserId, clock.GetUtcNow()), req.AttachmentId, ct);
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class DownloadLiveSoloRandomAttachmentRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionId { get; set; }
}
public sealed class DownloadLiveSoloRandomAttachmentEndpoint(AccessLiveSoloAttachments attachments, IUserContext user, TimeProvider clock)
    : Endpoint<DownloadLiveSoloRandomAttachmentRequest, Results<FileStreamHttpResult, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/attachment"); AuthSchemes("Bearer");
        Options(x => x.WithMetadata(new AttachmentBrowserDownloadMetadata(), new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(NoCTF.Application.Observability.ApiRequestKind.Download)));
        Description(x => x.WithName("DownloadLiveSoloRandomAttachment"));
        Summary(x => x.Summary = "Streams the team's immutable random attachment choice within this RoundQuestion.");
    }
    public override Task<Results<FileStreamHttpResult, NotFound, ProblemHttpResult>> ExecuteAsync(DownloadLiveSoloRandomAttachmentRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        return LiveSoloAttachmentTransport.OpenAsync(attachments, new(req.CompetitionId, req.MatchId, req.RoundId, req.QuestionId,
            user.UserId, clock.GetUtcNow()), null, ct);
    }
}

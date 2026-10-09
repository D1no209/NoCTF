using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloProgramSegmentRequest { public Guid CompetitionId { get; set; } public Guid MatchId { get; set; } public Guid SegmentId { get; set; } }
public sealed class GetLiveSoloProgramSegmentEndpoint(ILiveSoloProgramReader programs, IUserContext user)
    : Endpoint<GetLiveSoloProgramSegmentRequest, Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/program/segments/{segmentId}"); AllowAnonymous();
        Description(x => x.WithName("GetLiveSoloProgramSegment")); Summary(x => x.Summary = "Streams a delayed video segment after rechecking publication time and competition access.");
    }
    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(GetLiveSoloProgramSegmentRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var content = await programs.OpenSegmentAsync(req.CompetitionId, req.MatchId, req.SegmentId, user.UserId, ct);
        return content is null ? TypedResults.NotFound() : TypedResults.Stream(content.Content, content.ContentType, enableRangeProcessing: true);
    }
}

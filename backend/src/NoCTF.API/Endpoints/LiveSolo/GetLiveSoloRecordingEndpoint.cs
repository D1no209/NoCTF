using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloRecordingRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RecordingId { get; set; }
    public bool Download { get; set; }
}
public sealed class GetLiveSoloRecordingEndpoint(ILiveSoloRecordingStore recordings, IUserContext user)
    : Endpoint<GetLiveSoloRecordingRequest, Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/recordings/{recordingId}/file"); AllowAnonymous();
        Options(x => x.WithMetadata(new LiveSoloRecordingBrowserAccessMetadata()));
        Description(x => x.WithName("GetLiveSoloRecording"));
        Summary(x => x.Summary = "Streams an authorized recording with Range support after rechecking its current publication, hold and retention state.");
    }
    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(GetLiveSoloRecordingRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
        var result = await recordings.OpenAsync(req.CompetitionId, req.MatchId, req.RecordingId, user.UserId, ct);
        if (result is not null && !LiveSoloRecordingBrowserAccess.MatchesFile(HttpContext, result.FileId))
        { await result.Content.DisposeAsync(); return TypedResults.NotFound(); }
        return result is null ? TypedResults.NotFound() : TypedResults.Stream(result.Content, result.ContentType,
            req.Download ? result.FileName : null, enableRangeProcessing: true);
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class PrepareLiveSoloRecordingDownloadRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RecordingId { get; set; }
}
public sealed class PrepareLiveSoloRecordingDownloadEndpoint(ILiveSoloRecordingStore recordings, LiveSoloRecordingBrowserAccess browser, IUserContext user)
    : Endpoint<PrepareLiveSoloRecordingDownloadRequest, Results<Ok<AttachmentBrowserDownloadResponse>, NotFound>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/recordings/{recordingId}/browser-download"); AuthSchemes("Bearer");
        Description(x => x.WithName("PrepareLiveSoloRecordingDownload"));
        Summary(x => x.Summary = "Authorizes native browser streaming without buffering the recording in the SPA.");
    }
    public override async Task<Results<Ok<AttachmentBrowserDownloadResponse>, NotFound>> ExecuteAsync(PrepareLiveSoloRecordingDownloadRequest req, CancellationToken ct)
    {
        var fileId = await recordings.AuthorizeFileAsync(req.CompetitionId, req.MatchId, req.RecordingId, user.UserId, ct);
        if (fileId is null) return TypedResults.NotFound();
        var path = $"/api/v1/competitions/{req.CompetitionId}/live-solo/matches/{req.MatchId}/recordings/{req.RecordingId}/file";
        browser.Write(HttpContext, path, fileId.Value); return TypedResults.Ok(new AttachmentBrowserDownloadResponse(path + "?download=true"));
    }
}

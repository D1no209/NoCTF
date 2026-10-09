using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class PrepareLiveSoloRecordingPreviewRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RecordingId { get; set; }
}
public sealed record LiveSoloRecordingPreviewResponse(string PreviewUrl, DateTimeOffset ExpiresAt);
public sealed class PrepareLiveSoloRecordingPreviewEndpoint(ILiveSoloRecordingStore recordings, LiveSoloRecordingBrowserAccess browser, IUserContext user)
    : Endpoint<PrepareLiveSoloRecordingPreviewRequest, Results<Ok<LiveSoloRecordingPreviewResponse>, NotFound>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/recordings/{recordingId}/preview"); AuthSchemes("Bearer");
        Description(x => x.WithName("PrepareLiveSoloRecordingPreview"));
        Summary(x => x.Summary = "Issues a short-lived file-bound browser grant; preview ranges revalidate current JWT, MFA and recording authorization.");
    }
    public override async Task<Results<Ok<LiveSoloRecordingPreviewResponse>, NotFound>> ExecuteAsync(PrepareLiveSoloRecordingPreviewRequest req, CancellationToken ct)
    {
        var fileId = await recordings.AuthorizeFileAsync(req.CompetitionId, req.MatchId, req.RecordingId, user.UserId, ct);
        if (fileId is null) return TypedResults.NotFound();
        var path = $"/api/v1/competitions/{req.CompetitionId}/live-solo/matches/{req.MatchId}/recordings/{req.RecordingId}/file";
        return TypedResults.Ok(new LiveSoloRecordingPreviewResponse(path, browser.Write(HttpContext, path, fileId.Value)));
    }
}

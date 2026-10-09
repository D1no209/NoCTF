using System.Globalization;
using System.Text;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloProgramPlaylistRequest { public Guid CompetitionId { get; set; } public Guid MatchId { get; set; } }
public sealed class GetLiveSoloProgramPlaylistEndpoint(ILiveSoloProgramReader programs, IUserContext user)
    : Endpoint<GetLiveSoloProgramPlaylistRequest, Results<ContentHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/program/playlist"); AllowAnonymous();
        Description(x => x.WithName("GetLiveSoloProgramPlaylist")); Summary(x => x.Summary = "Builds HLS exclusively from server-published segments without exposing the capture spool or raw SFU.");
    }
    public override async Task<Results<ContentHttpResult, NotFound>> ExecuteAsync(GetLiveSoloProgramPlaylistRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var program = await programs.ReadAsync(req.CompetitionId, req.MatchId, user.UserId, ct);
        if (program is null || program.Segments.Count == 0) return TypedResults.NotFound();
        var target = Math.Max(1, (int)Math.Ceiling(program.Segments.Max(x => x.Duration.TotalSeconds)));
        var text = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n").Append("#EXT-X-TARGETDURATION:").Append(target.ToString(CultureInfo.InvariantCulture))
            .Append("\n#EXT-X-MEDIA-SEQUENCE:").Append(program.Segments[0].Sequence.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var segment in program.Segments)
            text.Append("#EXTINF:").Append(segment.Duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append(",\n")
                .Append("segments/").Append(segment.Id.ToString("D")).Append('\n');
        if (program.Ended) text.Append("#EXT-X-ENDLIST\n");
        return TypedResults.Text(text.ToString(), "application/vnd.apple.mpegurl", Encoding.UTF8);
    }
}

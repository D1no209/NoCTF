using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed record LiveSoloRoundResponse(Guid Id, Guid MatchId, int Number, int Replay,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloRoundState>))] LiveSoloRoundState State,
    Guid ConcurrencyStamp, long TimelineRevision, DateTimeOffset? CountdownAt, DateTimeOffset? StartedAt,
    int LimitSeconds, long ActiveElapsedMilliseconds, bool Paused, Guid? WinnerTeamId, Guid? WinningGameplayFactId);
internal static class LiveSoloRoundProtocol
{
    public static LiveSoloRoundResponse Round(LiveSoloRoundView value) => new(value.Id, value.MatchId, value.Number, value.Replay, value.State,
        value.ConcurrencyStamp, value.TimelineRevision, value.CountdownAt, value.StartedAt, value.LimitSeconds,
        value.ActiveElapsedMilliseconds, value.Paused, value.WinnerTeamId, value.WinningGameplayFactId);
}
public sealed class GetLiveSoloRoundRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public bool Staff { get; set; }
}
public sealed class GetLiveSoloRoundEndpoint(ILiveSoloMatchStore matches, IUserContext user, TimeProvider clock)
    : Endpoint<GetLiveSoloRoundRequest, Results<Ok<LiveSoloRoundResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}"); AuthSchemes("Bearer");
        Description(x => x.WithName("GetLiveSoloRound"));
        Summary(x => x.Summary = "Reads the authorized Round clock and durable result, without unopened questions.");
    }
    public override async Task<Results<Ok<LiveSoloRoundResponse>, NotFound>> ExecuteAsync(GetLiveSoloRoundRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await matches.FindRoundAsync(req.CompetitionId, req.MatchId, req.RoundId, user.UserId, req.Staff, clock.GetUtcNow(), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(LiveSoloRoundProtocol.Round(result));
    }
}

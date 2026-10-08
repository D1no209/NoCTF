using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Brackets;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed record LiveSoloBracketSourceResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloSide>))] LiveSoloSide Side,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloSlotSource>))] LiveSoloSlotSource Source,
    int? Seed, Guid? SourceMatchId, bool Resolved, Guid? TeamId);
public sealed record LiveSoloBracketMatchResponse(LiveSoloMatchResponse Match,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloBracketLane>))] LiveSoloBracketLane Lane,
    int Stage, int Position, bool Conditional, IReadOnlyList<LiveSoloBracketSourceResponse> Sources);
public sealed record LiveSoloBracketResponse(Guid CompetitionId, Guid ConcurrencyStamp,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloBracketFormat>))] LiveSoloBracketFormat Format,
    IReadOnlyList<LiveSoloBracketMatchResponse> Matches, Guid? ChampionTeamId);
internal static class LiveSoloBracketProtocol
{
    public static LiveSoloBracketResponse Map(LiveSoloBracketView view) => new(view.CompetitionId, view.ConcurrencyStamp, view.Format,
        view.Matches.Select(x => new LiveSoloBracketMatchResponse(LiveSoloProtocol.Match(x.Match), x.Lane, x.Stage, x.Position, x.Conditional,
            x.Sources.Select(s => new LiveSoloBracketSourceResponse(s.Side, s.Source, s.Seed, s.SourceMatchId, s.Resolved, s.TeamId)).ToArray())).ToArray(), view.ChampionTeamId);
}
public sealed class GetLiveSoloBracketRequest { public Guid CompetitionId { get; set; } }
public sealed class GetLiveSoloBracketEndpoint(ManageLiveSoloBracket brackets, IUserContext user)
    : Endpoint<GetLiveSoloBracketRequest, Results<Ok<LiveSoloBracketResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/bracket"); AuthSchemes("Bearer");
        Description(x => x.WithName("GetLiveSoloBracket"));
        Summary(x => x.Summary = "Reads the authorized staff bracket and seed/source graph; public delayed projections are separate.");
    }
    public override async Task<Results<Ok<LiveSoloBracketResponse>, NotFound>> ExecuteAsync(GetLiveSoloBracketRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await brackets.ReadAsync(req.CompetitionId, user.UserId, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(LiveSoloBracketProtocol.Map(result));
    }
}

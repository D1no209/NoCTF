using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed record LiveSoloRosterResponse(Guid TeamId, IReadOnlyList<Guid> UserIds, bool Locked, bool Ready);
public sealed record LiveSoloMatchResponse(Guid Id, Guid CompetitionId,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloMatchState>))] LiveSoloMatchState State,
    Guid ConcurrencyStamp, int RequiredWins, int LeftWins, int RightWins, Guid? LeftTeamId, string? LeftTeamName,
    Guid? RightTeamId, string? RightTeamName, Guid? CurrentRoundId, Guid? WinnerTeamId, IReadOnlyList<LiveSoloRosterResponse> Rosters,
    Guid? PendingCorrectionId = null, Guid? ReplacementMatchId = null, Guid? PendingCorrectionMatchId = null);
public sealed record LiveSoloFailureResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloFailure>))] LiveSoloFailure Code)
{
    public string Detail => ApiMessages.Text(ApiMessages.For(Code).Id);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

internal static class LiveSoloProtocol
{
    public static LiveSoloMatchResponse Match(LiveSoloMatchView value) => new(value.Id, value.CompetitionId, value.State,
        value.ConcurrencyStamp, value.RequiredWins, value.LeftWins, value.RightWins, value.LeftTeamId, value.LeftTeamName,
        value.RightTeamId, value.RightTeamName, value.CurrentRoundId, value.WinnerTeamId,
        value.Rosters.Select(x => new LiveSoloRosterResponse(x.TeamId, x.UserIds, x.Locked, x.Ready)).ToArray(),
        value.PendingCorrectionId, value.ReplacementMatchId, value.PendingCorrectionMatchId);

    public static Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>,
        UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult> MatchMutation(LiveSoloMatchResult result) => result.Failure switch
    {
        null when result.Match is not null => TypedResults.Ok(Match(result.Match)),
        LiveSoloFailure.NotFound => TypedResults.NotFound(),
        LiveSoloFailure.Forbidden => TypedResults.Forbid(),
        LiveSoloFailure.InvalidConfiguration => TypedResults.UnprocessableEntity(new LiveSoloFailureResponse(result.Failure.Value)),
        LiveSoloFailure.DependencyUnavailable => Unavailable(result.Failure.Value),
        _ => TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure ?? LiveSoloFailure.Conflict))
    };

    public static Results<Ok<LiveSoloRoundResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>,
        UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult> RoundMutation(LiveSoloRoundResult result) => result.Failure switch
    {
        null when result.Round is not null => TypedResults.Ok(LiveSoloRoundProtocol.Round(result.Round)),
        LiveSoloFailure.NotFound => TypedResults.NotFound(),
        LiveSoloFailure.Forbidden => TypedResults.Forbid(),
        LiveSoloFailure.InvalidConfiguration => TypedResults.UnprocessableEntity(new LiveSoloFailureResponse(result.Failure.Value)),
        LiveSoloFailure.DependencyUnavailable => Unavailable(result.Failure.Value),
        _ => TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure ?? LiveSoloFailure.Conflict))
    };
    public static ProblemHttpResult Unavailable(LiveSoloFailure failure) => ApiProblems.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: ApiMessages.For(failure), extensions: new Dictionary<string, object?> { ["code"] = failure.ToString() });
}

public sealed class GetLiveSoloMatchRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public bool Staff { get; set; }
}
public sealed class GetLiveSoloMatchEndpoint(ILiveSoloMatchStore matches, IUserContext user, TimeProvider clock)
    : Endpoint<GetLiveSoloMatchRequest, Results<Ok<LiveSoloMatchResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}"); AuthSchemes("Bearer");
        Description(x => x.WithName("GetLiveSoloMatch"));
        Summary(x => x.Summary = "Reads a participating team's Match or an authorized staff projection.");
    }
    public override async Task<Results<Ok<LiveSoloMatchResponse>, NotFound>> ExecuteAsync(GetLiveSoloMatchRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await matches.FindAsync(req.CompetitionId, req.MatchId, user.UserId, req.Staff, clock.GetUtcNow(), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(LiveSoloProtocol.Match(result));
    }
}

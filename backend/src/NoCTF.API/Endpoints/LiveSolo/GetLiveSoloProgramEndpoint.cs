using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloProgramRequest { public Guid CompetitionId { get; set; } public Guid MatchId { get; set; } }
public sealed record LiveSoloProgramQuestionResponse(Guid Id, Guid CompetitionChallengeId, int Position, string Title, DateTimeOffset OpenedAt);
public sealed record LiveSoloProgramStateResponse(DateTimeOffset AsOf,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloMatchState>))] LiveSoloMatchState MatchState,
    int RequiredWins, int LeftWins, int RightWins, Guid? LeftTeamId, Guid? RightTeamId, string? LeftTeamName, string? RightTeamName,
    Guid? RoundId, int? RoundNumber,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloRoundState>))] LiveSoloRoundState? RoundState,
    long? TimelineRevision, long ActiveElapsedMilliseconds, int? LimitSeconds, bool Paused, IReadOnlyList<LiveSoloProgramQuestionResponse> Questions)
{
    internal static LiveSoloProgramStateResponse From(LiveSoloProgramStateView state) => new(state.AsOf, state.MatchState, state.RequiredWins, state.LeftWins, state.RightWins,
        state.LeftTeamId, state.RightTeamId, state.LeftTeamName, state.RightTeamName, state.RoundId, state.RoundNumber, state.RoundState,
        state.TimelineRevision, state.ActiveElapsedMilliseconds, state.LimitSeconds, state.Paused, state.Questions.Select(x => new LiveSoloProgramQuestionResponse(x.Id, x.CompetitionChallengeId, x.Position, x.Title, x.OpenedAt)).ToArray());
}
public sealed record LiveSoloProgramSegmentResponse(Guid Id, long Sequence, double DurationSeconds, LiveSoloProgramStateResponse State);
public sealed record LiveSoloProgramResponse(Guid ProgramCaptureId, int DelaySeconds, IReadOnlyList<LiveSoloProgramSegmentResponse> Segments, bool Ended, string PlaylistUrl);
public sealed class GetLiveSoloProgramEndpoint(ILiveSoloProgramReader programs, IUserContext user, LiveSoloViewerBrowserAccess browser)
    : Endpoint<GetLiveSoloProgramRequest, Results<Ok<LiveSoloProgramResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/program"); AllowAnonymous();
        Description(x => x.WithName("GetLiveSoloProgram").WithMetadata(new LiveSoloViewerBrowserAccessMetadata())); Summary(x => x.Summary = "Reads only published delayed video segments and their immutable matching state frames.");
    }
    public override async Task<Results<Ok<LiveSoloProgramResponse>, NotFound>> ExecuteAsync(GetLiveSoloProgramRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (browser.LeaseId(HttpContext, req.CompetitionId, req.MatchId) == Guid.Empty) return TypedResults.NotFound();
        var program = await programs.ReadAsync(req.CompetitionId, req.MatchId, user.UserId, browser.LeaseId(HttpContext, req.CompetitionId, req.MatchId), ct);
        return program is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloProgramResponse(program.ProgramCaptureId, program.DelaySeconds,
            program.Segments.Select(x => new LiveSoloProgramSegmentResponse(x.Id, x.Sequence, x.Duration.TotalSeconds, LiveSoloProgramStateResponse.From(x.State))).ToArray(), program.Ended,
            $"/api/v1/competitions/{req.CompetitionId:D}/live-solo/matches/{req.MatchId:D}/program/playlist"));
    }
}

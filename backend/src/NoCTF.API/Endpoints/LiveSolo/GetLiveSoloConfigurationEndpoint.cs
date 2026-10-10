using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloBracketFormatProtocol>))]
public enum LiveSoloBracketFormatProtocol { SingleElimination, DoubleElimination }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloBracketLaneProtocol>))]
public enum LiveSoloBracketLaneProtocol { Winners, Losers, GrandFinal, ResetFinal }
public sealed record LiveSoloStageRuleContract(LiveSoloBracketLaneProtocol Lane, int Stage, int RequiredWins);
public sealed record LiveSoloConfigurationContract(bool Enabled, bool PlatformStreamingEnabled, LiveSoloBracketFormatProtocol BracketFormat,
    int RequiredWins, int CountdownSeconds, int QuestionIntervalSeconds, int RoundLimitSeconds,
    int PublicDelaySeconds, bool ParticipantsMayViewOpponents, bool RecordingEnabled, int RecordingRetentionDays,
    int MaximumConcurrentMatches, int MaximumRosterMembers, int MaximumViewers, IReadOnlyList<LiveSoloStageRuleContract> StageRules);

public static class LiveSoloConfigurationMapping
{
    public static LiveSoloConfigurationContract ToContract(LiveSoloCompetitionModeConfiguration value) => new(value.Enabled, value.PlatformStreamingEnabled,
        (LiveSoloBracketFormatProtocol)value.BracketFormat, value.RequiredWins, value.CountdownSeconds,
        value.QuestionIntervalSeconds, value.RoundLimitSeconds, value.PublicDelaySeconds, value.ParticipantsMayViewOpponents,
        value.RecordingEnabled, value.RecordingRetentionDays, value.MaximumConcurrentMatches, value.MaximumRosterMembers,
        value.MaximumViewers, value.StageRules.OrderBy(x => x.Lane).ThenBy(x => x.Stage)
            .Select(x => new LiveSoloStageRuleContract((LiveSoloBracketLaneProtocol)x.Lane, x.Stage, x.RequiredWins)).ToArray());
    public static LiveSoloCompetitionModeConfiguration ToDomain(Guid competitionId, LiveSoloConfigurationContract value) => new()
    {
        CompetitionId = competitionId, Enabled = value.Enabled, PlatformStreamingEnabled = value.PlatformStreamingEnabled, BracketFormat = (LiveSoloBracketFormat)value.BracketFormat,
        RequiredWins = value.RequiredWins, CountdownSeconds = value.CountdownSeconds, QuestionIntervalSeconds = value.QuestionIntervalSeconds,
        RoundLimitSeconds = value.RoundLimitSeconds, PublicDelaySeconds = value.PublicDelaySeconds,
        ParticipantsMayViewOpponents = value.ParticipantsMayViewOpponents, RecordingEnabled = value.RecordingEnabled,
        RecordingRetentionDays = value.RecordingRetentionDays, MaximumConcurrentMatches = value.MaximumConcurrentMatches,
        MaximumRosterMembers = value.MaximumRosterMembers, MaximumViewers = value.MaximumViewers,
        StageRules = value.StageRules.Select(x => new LiveSoloStageRule { CompetitionId = competitionId,
            Lane = (LiveSoloBracketLane)x.Lane, Stage = x.Stage, RequiredWins = x.RequiredWins }).ToList()
    };
}
public sealed class GetLiveSoloConfigurationRequest { public Guid CompetitionId { get; set; } }
public sealed class GetLiveSoloConfigurationEndpoint(GetCompetitionConfiguration configuration,
    ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : Endpoint<GetLiveSoloConfigurationRequest, Results<Ok<LiveSoloConfigurationContract>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/configuration"); AuthSchemes("Bearer");
        Description(x => x.WithName("GetLiveSoloConfiguration").WithDescription("Staff configuration only; player and public state use separately authorized projections."));
        Summary(x => x.Summary = "Reads the independent LiveSolo match, timing and media policy.");
    }
    public override async Task<Results<Ok<LiveSoloConfigurationContract>, NotFound, ForbidHttpResult>> ExecuteAsync(GetLiveSoloConfigurationRequest req, CancellationToken ct)
    {
        if (!await authorizer.CanObserveAsync(user.UserId, req.CompetitionId, ct)) return TypedResults.Forbid();
        var result = await configuration.ExecuteAsync(req.CompetitionId, ct);
        return result?.Configuration is LiveSoloCompetitionModeConfiguration liveSolo
            ? TypedResults.Ok(LiveSoloConfigurationMapping.ToContract(liveSolo)) : TypedResults.NotFound();
    }
}

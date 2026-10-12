using System.Globalization;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Competitions;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<SpectatorHealthProtocol>))]
public enum SpectatorHealthProtocol { Unknown, Healthy, Down }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<SpectatorChangeProtocol>))]
public enum SpectatorChangeProtocol { Attack, Control, Uncontrolled, DetectionFailed }

public sealed record SpectatorServiceResponse(Guid TeamId, Guid ChallengeId, SpectatorHealthProtocol Health, DateTimeOffset? CheckedAt);
public sealed record SpectatorPointResponse(Guid ChallengeId, Guid? ControllerId, Guid? ObservedTeamId,
    SpectatorHealthProtocol Health, DateTimeOffset? CheckedAt, int IntervalSeconds, long PointsPerInterval);
public sealed record SpectatorChangeResponse(Guid Id, Guid Revision, DateTimeOffset At,
    SpectatorChangeProtocol Kind, Guid ChallengeId, Guid? TeamId, Guid? TargetTeamId, Guid? ControllerId);
public sealed record SpectatorRoundResponse(Guid Id, int Number, DateTimeOffset StartAt, DateTimeOffset EndAt);
public sealed record SpectatorScreenResponse(string Version, DateTimeOffset DataAsOf, DateTimeOffset ServerTime,
    LeaderboardDataScopeProtocol Scope, GameModeProtocol Mode, string? CompetitionName, CompetitionStatusProtocol? Status,
    DateTimeOffset? HardeningEndsAt, IReadOnlyList<SpectatorScreenTeam> Teams,
    IReadOnlyList<ScoreboardChallengeCatalogItem> Challenges, SpectatorRoundResponse? Round,
    IReadOnlyList<SpectatorServiceResponse> Services, IReadOnlyList<SpectatorPointResponse> Points,
    IReadOnlyList<SpectatorChangeResponse> Changes, IReadOnlyList<SpectatorSettlement> Settlements,
    DateTimeOffset? HistoryStartsAt, bool HistoryTruncated, string? NextCursor);

public class GetAwdSpectatorRequest
{
    public Guid CompetitionId { get; set; }
    [QueryParam] public int Minutes { get; set; } = 10;
    [QueryParam] public string? Cursor { get; set; }
}
public sealed class GetAwdSpectatorValidator : Validator<GetAwdSpectatorRequest>
{
    public GetAwdSpectatorValidator()
    {
        RuleFor(x => x.Minutes).Must(x => x is 5 or 10 or 30);
        RuleFor(x => x.Cursor).MaximumLength(100);
    }
}

public sealed class GetAwdSpectatorEndpoint(GetSpectatorScreen query)
    : Endpoint<GetAwdSpectatorRequest, Results<Ok<SpectatorScreenResponse>, Accepted, NotFound, Conflict>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/awd-spectator");
        AllowAnonymous();
        Summary(s => s.Summary = "Read a public-only, consistent AWD broadcast snapshot and bounded history.");
    }

    public override async Task<Results<Ok<SpectatorScreenResponse>, Accepted, NotFound, Conflict>> ExecuteAsync(
        GetAwdSpectatorRequest request, CancellationToken ct) => SpectatorProtocol.Map(await query.ExecuteAsync(
            Route<Guid>("competitionId"), GameMode.Awd, request.Minutes, request.Cursor, ct));
}

internal static class SpectatorProtocol
{
    public static Results<Ok<SpectatorScreenResponse>, Accepted, NotFound, Conflict> Map(SpectatorReadResult result)
    {
        if (result.State == SpectatorReadState.NotFound) return TypedResults.NotFound();
        if (result.State == SpectatorReadState.CursorExpired) return TypedResults.Conflict();
        if (result.Screen is not { } x) return TypedResults.Accepted((string?)null);
        var f = x.Frame;
        return TypedResults.Ok(new SpectatorScreenResponse(x.Version.ToString(CultureInfo.InvariantCulture), x.DataAsOf, x.ServerTime,
            ScoreboardProtocolMapper.ToProtocol(x.Scope), CompetitionProtocolMapper.ToProtocol(x.Mode),
            f?.CompetitionName, f is null ? null : CompetitionProtocolMapper.ToProtocol(f.Status), f?.HardeningEndsAt,
            x.Teams, x.Challenges, x.Round is { } r ? new(r.Id, r.Number, r.StartAt, r.EndAt) : null,
            f?.Services.Select(s => new SpectatorServiceResponse(s.TeamId, s.ChallengeId, Health(s.Health), s.CheckedAt)).ToArray() ?? [],
            f?.Points.Select(p => new SpectatorPointResponse(p.ChallengeId, p.ControllerId, p.ObservedTeamId, Health(p.Health), p.CheckedAt, p.IntervalSeconds, p.PointsPerInterval)).ToArray() ?? [],
            f?.Changes.Select(c => new SpectatorChangeResponse(c.Id, c.Revision, c.At, Kind(c.Kind), c.ChallengeId, c.TeamId, c.TargetTeamId, c.ControllerId)).ToArray() ?? [],
            x.Settlements, f?.HistoryStartsAt, f?.HistoryTruncated ?? false, x.NextCursor));
    }
    private static SpectatorHealthProtocol Health(SpectatorHealth value) => value switch
    {
        SpectatorHealth.Healthy => SpectatorHealthProtocol.Healthy,
        SpectatorHealth.Down => SpectatorHealthProtocol.Down,
        _ => SpectatorHealthProtocol.Unknown
    };
    private static SpectatorChangeProtocol Kind(SpectatorChangeKind value) => value switch
    {
        SpectatorChangeKind.Attack => SpectatorChangeProtocol.Attack,
        SpectatorChangeKind.Control => SpectatorChangeProtocol.Control,
        SpectatorChangeKind.Uncontrolled => SpectatorChangeProtocol.Uncontrolled,
        _ => SpectatorChangeProtocol.DetectionFailed
    };
}


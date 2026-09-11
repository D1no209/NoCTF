using System.Globalization;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Competitions;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardProblemCode>))]
internal enum LeaderboardProblemCode { LeaderboardProjectionFailed }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardDataScopeProtocol>))]
public enum LeaderboardDataScopeProtocol { Live, Frozen, Hidden }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardProjectionStateProtocol>))]
public enum LeaderboardProjectionStateProtocol { Processing }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardRankingStateProtocol>))]
public enum ScoreboardRankingStateProtocol { Eligible, Banned, Disqualified }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardScoreStateProtocol>))]
public enum ScoreboardScoreStateProtocol { Pending, Provisional, Settled }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardOperationStateProtocol>))]
public enum ScoreboardOperationStateProtocol { None, Failed, Succeeded }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardBreakdownKindProtocol>))]
public enum ScoreboardBreakdownKindProtocol
{
    Solve, Attack, Defense, Availability, Control, Penalty, BloodAward, Hint, ManualAdjustment
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardEntryKindProtocol>))]
public enum ScoreboardEntryKindProtocol
{
    Solve, Attack, Defense, Availability, Control, Penalty, BloodAward, Hint, ManualAdjustment
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardEntryOutcomeProtocol>))]
public enum ScoreboardEntryOutcomeProtocol { Pending, Succeeded, Failed, Rejected }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardAwardProtocol>))]
public enum ScoreboardAwardProtocol { FirstBlood, SecondBlood, ThirdBlood }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardAdjustmentKindProtocol>))]
public enum ScoreboardAdjustmentKindProtocol { ManualAdjustment, CompetitionPenalty, BanRecalculation }

public sealed record ScoreboardActorResponse(int Index, Guid UserId, string DisplayName);

public sealed record ScoreboardBreakdownResponse(
    ScoreboardBreakdownKindProtocol Kind,
    int SuccessfulCount,
    int AttemptCount,
    long EarnedPoints,
    long DeductedPoints,
    long NetPoints);

public sealed record ScoreboardEntryResponse(
    Guid Id,
    ScoreboardEntryKindProtocol Kind,
    ScoreboardEntryOutcomeProtocol Outcome,
    int? ActorIndex,
    Guid? TargetTeamId,
    DateTimeOffset OccurredAt,
    DateTimeOffset? SettledAt,
    long? EarnedPoints,
    long? DeductedPoints,
    long? NetPoints,
    ScoreboardAwardProtocol? Award,
    long AwardPoints);

public sealed record ScoreboardAdjustmentResponse(
    Guid Id,
    ScoreboardAdjustmentKindProtocol Kind,
    DateTimeOffset OccurredAt,
    int? ActorIndex,
    long EarnedPoints,
    long DeductedPoints,
    long NetPoints);

public sealed record ScoreboardSlotResponse(
    int ColumnIndex,
    ScoreboardScoreStateProtocol ScoreState,
    long? EarnedPoints,
    long? DeductedPoints,
    long? NetPoints,
    int EntryCount,
    IReadOnlyList<ScoreboardBreakdownResponse> Breakdown,
    IReadOnlyList<ScoreboardEntryResponse> Entries,
    ScoreboardOperationStateProtocol OffenseState,
    ScoreboardOperationStateProtocol DefenseState);

public sealed record ScoreboardChallengeScoreResponse(
    Guid CompetitionChallengeId,
    long AttackScore,
    long DefenseScore);

public sealed record ScoreboardMemberContributionResponse(
    Guid UserId,
    string DisplayName,
    long EarnedPoints);

public sealed record ScoreboardChallengeAchievementResponse(Guid CompetitionChallengeId,
    ScoreboardEntryKindProtocol Kind, Guid? UserId, string? DisplayName, DateTimeOffset OccurredAt);

public sealed record ScoreboardTeamResponse(
    Guid TeamId,
    string TeamName,
    string TrackKey,
    int? Rank,
    ScoreboardRankingStateProtocol RankingState,
    long TotalScore,
    long ScoreOutsideWindow,
    long? AttackScore,
    long? DefenseScore,
    IReadOnlyList<ScoreboardChallengeScoreResponse> ChallengeScores,
    IReadOnlyList<ScoreboardMemberContributionResponse> MemberContributions,
    int GlobalAdjustmentCount,
    IReadOnlyList<ScoreboardAdjustmentResponse> GlobalAdjustments,
    IReadOnlyList<ScoreboardSlotResponse> Slots)
{
    public IReadOnlyList<ScoreboardChallengeAchievementResponse>? Achievements { get; init; }
}

public sealed record ScoreboardTrackResponse(
    string Key,
    string Name,
    bool IsInternal,
    bool VisibleOnLeaderboard,
    bool IsViewerTrack);

public sealed record ScoreboardCurrentChallengeScoreResponse(
    Guid CompetitionChallengeId,
    long? Score,
    long? BreakScore,
    long? FixScore);

public sealed record ScoreboardSnapshotResponse(
    Guid CompetitionId,
    string Version,
    string SchemaRevision,
    DateTimeOffset GeneratedAt,
    Guid? CurrentRoundId,
    IReadOnlyList<ScoreboardActorResponse> Actors,
    IReadOnlyList<ScoreboardTeamResponse> Teams)
{
    public IReadOnlyList<ScoreboardTrackResponse> Tracks { get; init; } = [];
    public bool TracksEnabled { get; init; } = true;
    public IReadOnlyList<ScoreboardCurrentChallengeScoreResponse> CurrentChallengeScores { get; init; } = [];
    public LeaderboardVisibilityProtocol Visibility { get; init; }
    public LeaderboardDataScopeProtocol DataScope { get; init; }
    public DateTimeOffset? DataAsOf { get; init; }
}

public sealed record LeaderboardProcessingProtocolResponse(
    Guid CompetitionId,
    LeaderboardProjectionStateProtocol State,
    string StatusUrl);

[Mapper]
internal static partial class ScoreboardProtocolMapper
{
    public static ScoreboardSnapshotResponse ToResponse(ScoreboardSnapshot value) => new(
        value.CompetitionId,
        value.Version.ToString(CultureInfo.InvariantCulture),
        value.SchemaRevision.ToString(CultureInfo.InvariantCulture),
        value.GeneratedAt,
        value.CurrentRoundId,
        value.Actors.Select(actor => new ScoreboardActorResponse(
            actor.Index, actor.UserId, actor.DisplayName)).ToArray(),
        value.Teams.Select(ToResponse).ToArray())
    {
        Tracks = value.Tracks.Select(track => new ScoreboardTrackResponse(
            track.Key, track.Name, track.IsInternal, track.VisibleOnLeaderboard,
            track.IsViewerTrack)).ToArray(),
        TracksEnabled = value.TracksEnabled,
        CurrentChallengeScores = value.CurrentChallengeScores.Select(score =>
            new ScoreboardCurrentChallengeScoreResponse(
                score.CompetitionChallengeId,
                score.Score,
                score.BreakScore,
                score.FixScore)).ToArray(),
        Visibility = CompetitionProtocolMapper.ToProtocol(value.Visibility),
        DataScope = ToProtocol(value.DataScope),
        DataAsOf = value.DataAsOf
    };

    public static ScoreboardEntryResponse ToResponse(ScoreboardSlotEntry value) => new(
        value.Id,
        ToProtocol(value.Kind),
        ToProtocol(value.Outcome),
        value.ActorIndex,
        value.TargetTeamId,
        value.OccurredAt,
        value.SettledAt,
        value.EarnedPoints,
        value.DeductedPoints,
        value.NetPoints,
        value.Award is null ? null : ToProtocol(value.Award.Value),
        value.AwardPoints);

    public static ScoreboardAdjustmentResponse ToResponse(ScoreboardAdjustment value) => new(
        value.Id,
        ToProtocol(value.Kind),
        value.OccurredAt,
        value.ActorIndex,
        value.EarnedPoints,
        value.DeductedPoints,
        value.NetPoints);

    private static ScoreboardTeamResponse ToResponse(ScoreboardTeam value) => new(
        value.TeamId,
        value.TeamName,
        value.TrackKey,
        value.Rank,
        ToProtocol(value.RankingState),
        value.TotalScore,
        value.ScoreOutsideWindow,
        value.AttackScore,
        value.DefenseScore,
        value.ChallengeScores.Select(item => new ScoreboardChallengeScoreResponse(
            item.CompetitionChallengeId,
            item.AttackScore,
            item.DefenseScore)).ToArray(),
        value.MemberContributions.Select(item => new ScoreboardMemberContributionResponse(
            item.UserId,
            item.DisplayName,
            item.EarnedPoints)).ToArray(),
        value.GlobalAdjustmentCount,
        value.GlobalAdjustments.Select(ToResponse).ToArray(),
        value.Slots.Select(slot => new ScoreboardSlotResponse(
            slot.ColumnIndex,
            ToProtocol(slot.ScoreState),
            slot.EarnedPoints,
            slot.DeductedPoints,
            slot.NetPoints,
            slot.EntryCount,
            slot.Breakdowns.Select(item => new ScoreboardBreakdownResponse(
                ToProtocol(item.Kind), item.SuccessfulCount, item.AttemptCount,
                item.EarnedPoints, item.DeductedPoints, item.NetPoints)).ToArray(),
            slot.Entries.Select(ToResponse).ToArray(),
            ToProtocol(slot.OffenseState),
            ToProtocol(slot.DefenseState))).ToArray())
    {
        Achievements = value.Achievements?.Select(item => new ScoreboardChallengeAchievementResponse(
            item.CompetitionChallengeId, ToProtocol(item.Kind), item.UserId, item.DisplayName, item.OccurredAt)).ToArray()
    };

    public static LeaderboardDataScopeProtocol ToProtocol(LeaderboardDataScope value) => value switch
    {
        LeaderboardDataScope.Live => LeaderboardDataScopeProtocol.Live,
        LeaderboardDataScope.Frozen => LeaderboardDataScopeProtocol.Frozen,
        LeaderboardDataScope.Hidden => LeaderboardDataScopeProtocol.Hidden,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial ScoreboardRankingStateProtocol ToProtocol(ScoreboardRankingState value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial ScoreboardScoreStateProtocol ToProtocol(ScoreboardScoreState value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial ScoreboardOperationStateProtocol ToProtocol(ScoreboardOperationState value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial ScoreboardBreakdownKindProtocol ToProtocol(ScoreboardBreakdownKind value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial ScoreboardEntryKindProtocol ToProtocol(ScoreboardEntryKind value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial ScoreboardEntryOutcomeProtocol ToProtocol(ScoreboardEntryOutcome value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial ScoreboardAwardProtocol ToProtocol(ScoreboardAward value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial ScoreboardAdjustmentKindProtocol ToProtocol(ScoreboardAdjustmentKind value);
}

public sealed class GetLeaderboardRequest
{
    public Guid CompetitionId { get; set; }

    [QueryParam]
    public int? EndingRound { get; set; }
}

public sealed class GetLeaderboardValidator : Validator<GetLeaderboardRequest>
{
    public GetLeaderboardValidator() =>
        RuleFor(request => request.EndingRound).GreaterThan(0).When(request => request.EndingRound is not null);
}

public sealed class GetLeaderboardEndpoint(
    ILeaderboardCache leaderboard,
    ILeaderboardSnapshotFactory snapshots,
    IBackendMessagePublisher messages,
    ICompetitionVisibilityAccess access,
    GetCompetitionTracks getTracks,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<GetLeaderboardRequest, Results<Ok<ScoreboardSnapshotResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Get the sparse scoreboard snapshot or queue a refresh.");
    }

    public override async Task<Results<Ok<ScoreboardSnapshotResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetLeaderboardRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var visibility = await access.ResolveAsync(
            user.UserId, request.CompetitionId, timeProvider.GetUtcNow(), cancellationToken);
        if (visibility is null)
            return TypedResults.NotFound();
        if (visibility.DataScope == LeaderboardDataScope.Hidden)
        {
            return TypedResults.Ok(ScoreboardProtocolMapper.ToResponse(new ScoreboardSnapshot(
                request.CompetitionId, 0, 0, timeProvider.GetUtcNow(), null, [], [])
            {
                TracksEnabled = visibility.TracksEnabled,
                Visibility = visibility.Visibility,
                DataScope = LeaderboardDataScope.Hidden
            }));
        }

        var projection = visibility.DataScope == LeaderboardDataScope.Frozen
            ? await leaderboard.GetFrozenScoreboardAsync(request.CompetitionId, cancellationToken)
            : await leaderboard.GetScoreboardAsync(request.CompetitionId, cancellationToken);
        if (projection is not null
            && visibility.DataScope != LeaderboardDataScope.Frozen
            && request.EndingRound is int endingRound
            && visibility.GameMode is NoCTF.Domain.Competitions.GameMode.Awdp
                or NoCTF.Domain.Competitions.GameMode.Awd)
        {
            var sourceSnapshot = projection.Snapshot;
            var window = await snapshots.CreateScoreboardWindowAsync(
                request.CompetitionId,
                endingRound,
                sourceSnapshot.DataAsOf ?? sourceSnapshot.GeneratedAt,
                cancellationToken);
            projection = window is null
                ? null
                : ScoreboardAudienceProjection.PreserveSnapshotScope(window, sourceSnapshot);
        }
        if (projection is not null)
        {
            var canObserve = user.UserId != Guid.Empty
                && await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, cancellationToken);
            var canViewInternalTracks = user.UserId != Guid.Empty
                && await authorizer.CanJudgeAsync(user.UserId, request.CompetitionId, cancellationToken);
            var tracks = await getTracks.ExecuteAsync(
                request.CompetitionId,
                user.UserId == Guid.Empty ? null : user.UserId,
                canViewInternalTracks,
                includeInvitationCodes: false,
                cancellationToken);
            if (tracks is null)
                return TypedResults.NotFound();
            projection = ScoreboardAudienceProjection.Filter(projection, canObserve);
            projection = ScoreboardAudienceProjection.FilterTracks(
                projection,
                tracks,
                canViewInternalTracks);
            var snapshot = projection.Snapshot with
            {
                Visibility = visibility.Visibility,
                DataScope = visibility.DataScope,
                DataAsOf = visibility.DataScope == LeaderboardDataScope.Frozen
                    ? projection.Snapshot.DataAsOf
                    : projection.Snapshot.GeneratedAt
            };
            return TypedResults.Ok(ScoreboardProtocolMapper.ToResponse(snapshot));
        }

        if (visibility.DataScope == LeaderboardDataScope.Frozen)
        {
            await messages.ApplyCompetitionVisibilityAsync(
                request.CompetitionId, timeProvider.GetUtcNow(), cancellationToken);
            return Processing(request.CompetitionId);
        }
        var status = await leaderboard.GetStatusAsync(request.CompetitionId, cancellationToken);
        if (status.LastFailureAt is not null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Leaderboard projection is unavailable.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = LeaderboardProblemCode.LeaderboardProjectionFailed
                });
        }
        await leaderboard.InvalidateAsync(request.CompetitionId, cancellationToken);
        return Processing(request.CompetitionId);
    }

    private Accepted<LeaderboardProcessingProtocolResponse> Processing(Guid competitionId)
    {
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{competitionId}/leaderboard";
        return TypedResults.Accepted(statusUrl, new LeaderboardProcessingProtocolResponse(
            competitionId, LeaderboardProjectionStateProtocol.Processing, statusUrl));
    }
}

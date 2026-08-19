using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
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
    IReadOnlyList<ScoreboardEntryResponse> Entries);

public sealed record ScoreboardTeamResponse(
    Guid TeamId,
    string TeamName,
    string TrackKey,
    int? Rank,
    ScoreboardRankingStateProtocol RankingState,
    long TotalScore,
    IReadOnlyList<ScoreboardAdjustmentResponse> GlobalAdjustments,
    IReadOnlyList<ScoreboardSlotResponse> Slots);

public sealed record ScoreboardTrackResponse(
    string Key,
    string Name,
    bool IsInternal,
    bool VisibleOnLeaderboard);

public sealed record ScoreboardSnapshotResponse(
    Guid CompetitionId,
    long Version,
    long SchemaRevision,
    DateTimeOffset GeneratedAt,
    Guid? CurrentRoundId,
    IReadOnlyList<ScoreboardActorResponse> Actors,
    IReadOnlyList<ScoreboardTeamResponse> Teams)
{
    public IReadOnlyList<ScoreboardTrackResponse> Tracks { get; init; } = [];
    public LeaderboardVisibilityProtocol Visibility { get; init; }
    public LeaderboardDataScopeProtocol DataScope { get; init; }
    public DateTimeOffset? DataAsOf { get; init; }
}

public sealed record LeaderboardProcessingProtocolResponse(
    Guid CompetitionId,
    LeaderboardProjectionStateProtocol State,
    string StatusUrl);

internal static class ScoreboardProtocolMapper
{
    public static ScoreboardSnapshotResponse ToResponse(ScoreboardSnapshot value) => new(
        value.CompetitionId,
        value.Version,
        value.SchemaRevision,
        value.GeneratedAt,
        value.CurrentRoundId,
        value.Actors.Select(actor => new ScoreboardActorResponse(
            actor.Index, actor.UserId, actor.DisplayName)).ToArray(),
        value.Teams.Select(ToResponse).ToArray())
    {
        Tracks = value.Tracks.Select(track => new ScoreboardTrackResponse(
            track.Key, track.Name, track.IsInternal, track.VisibleOnLeaderboard)).ToArray(),
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

    private static ScoreboardTeamResponse ToResponse(ScoreboardTeam value) => new(
        value.TeamId,
        value.TeamName,
        value.TrackKey,
        value.Rank,
        ToProtocol(value.RankingState),
        value.TotalScore,
        value.GlobalAdjustments.Select(adjustment => new ScoreboardAdjustmentResponse(
            adjustment.Id,
            ToProtocol(adjustment.Kind),
            adjustment.OccurredAt,
            adjustment.ActorIndex,
            adjustment.EarnedPoints,
            adjustment.DeductedPoints,
            adjustment.NetPoints)).ToArray(),
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
            slot.Entries.Select(ToResponse).ToArray())).ToArray());

    public static LeaderboardDataScopeProtocol ToProtocol(LeaderboardDataScope value) => value switch
    {
        LeaderboardDataScope.Live => LeaderboardDataScopeProtocol.Live,
        LeaderboardDataScope.Frozen => LeaderboardDataScopeProtocol.Frozen,
        LeaderboardDataScope.Hidden => LeaderboardDataScopeProtocol.Hidden,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static ScoreboardRankingStateProtocol ToProtocol(ScoreboardRankingState value) =>
        Enum.Parse<ScoreboardRankingStateProtocol>(value.ToString());
    private static ScoreboardScoreStateProtocol ToProtocol(ScoreboardScoreState value) =>
        Enum.Parse<ScoreboardScoreStateProtocol>(value.ToString());
    private static ScoreboardBreakdownKindProtocol ToProtocol(ScoreboardBreakdownKind value) =>
        Enum.Parse<ScoreboardBreakdownKindProtocol>(value.ToString());
    private static ScoreboardEntryKindProtocol ToProtocol(ScoreboardEntryKind value) =>
        Enum.Parse<ScoreboardEntryKindProtocol>(value.ToString());
    private static ScoreboardEntryOutcomeProtocol ToProtocol(ScoreboardEntryOutcome value) =>
        Enum.Parse<ScoreboardEntryOutcomeProtocol>(value.ToString());
    private static ScoreboardAwardProtocol ToProtocol(ScoreboardAward value) =>
        Enum.Parse<ScoreboardAwardProtocol>(value.ToString());
    private static ScoreboardAdjustmentKindProtocol ToProtocol(ScoreboardAdjustmentKind value) =>
        Enum.Parse<ScoreboardAdjustmentKindProtocol>(value.ToString());
}

internal static class ScoreboardAudienceProjection
{
    public static ScoreboardProjection Filter(ScoreboardProjection projection, bool canObserve)
    {
        if (canObserve)
            return projection;
        var challenges = projection.ChallengeCatalog.Challenges
            .Where(challenge => challenge.IsPublished)
            .ToArray();
        var challengeIds = challenges
            .Select(challenge => challenge.CompetitionChallengeId)
            .ToHashSet();
        var columns = projection.Schema.Columns
            .Where(column => challengeIds.Contains(column.CompetitionChallengeId))
            .OrderBy(column => column.Index)
            .ToArray();
        var indexMap = columns.Select((column, index) => (column.Index, NewIndex: index))
            .ToDictionary(item => item.Index, item => item.NewIndex);
        var catalogRevision = StableRevision(challenges.Select(challenge =>
            $"{challenge.CompetitionChallengeId:N}|{challenge.Revision}|{challenge.Order}"));
        var mappedColumns = columns.Select((column, index) => column with { Index = index }).ToArray();
        var schemaRevision = StableRevision(mappedColumns.Select(column =>
            $"{column.Index}|{column.CompetitionChallengeId:N}|{column.RoundId?.ToString("N")}"));
        var visibleTeams = projection.Snapshot.Teams.Select(team => team with
        {
            Slots = team.Slots
                .Where(slot => indexMap.ContainsKey(slot.ColumnIndex))
                .Select(slot => slot with { ColumnIndex = indexMap[slot.ColumnIndex] })
                .ToArray()
        }).ToArray();
        var actorIndexes = visibleTeams
            .SelectMany(team => team.GlobalAdjustments.Select(item => item.ActorIndex)
                .Concat(team.Slots.SelectMany(slot => slot.Entries.Select(entry => entry.ActorIndex))))
            .Where(index => index is not null)
            .Select(index => index!.Value)
            .ToHashSet();
        var actorPairs = projection.Snapshot.Actors
            .Where(actor => actorIndexes.Contains(actor.Index))
            .OrderBy(actor => actor.Index)
            .Select((actor, index) => new { OldIndex = actor.Index, Actor = actor with { Index = index } })
            .ToArray();
        var actors = actorPairs.Select(pair => pair.Actor).ToArray();
        var actorIndexMap = actorPairs.ToDictionary(pair => pair.OldIndex, pair => pair.Actor.Index);
        int? MapActor(int? actorIndex) => actorIndex is int value
            && actorIndexMap.TryGetValue(value, out var mapped)
                ? mapped
                : null;
        var teams = visibleTeams.Select(team => team with
        {
            GlobalAdjustments = team.GlobalAdjustments
                .Select(item => item with { ActorIndex = MapActor(item.ActorIndex) })
                .ToArray(),
            Slots = team.Slots
                .Select(slot => slot with
                {
                    Entries = slot.Entries.Select(entry => entry with
                    {
                        ActorIndex = MapActor(entry.ActorIndex)
                    }).ToArray()
                })
                .ToArray()
        }).ToArray();
        return projection with
        {
            ChallengeCatalog = projection.ChallengeCatalog with
            {
                Revision = catalogRevision,
                Challenges = challenges
            },
            Schema = projection.Schema with
            {
                Revision = schemaRevision,
                ChallengeCatalogRevision = catalogRevision,
                Columns = mappedColumns
            },
            Snapshot = projection.Snapshot with
            {
                SchemaRevision = schemaRevision,
                Actors = actors,
                Teams = teams
            }
        };
    }

    private static long StableRevision(IEnumerable<string> values)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values)));
        return Math.Max(1, BitConverter.ToInt64(bytes, 0) & long.MaxValue);
    }
}

public sealed class GetLeaderboardRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class GetLeaderboardEndpoint(
    ILeaderboardCache leaderboard,
    IBackendMessagePublisher messages,
    ICompetitionVisibilityAccess access,
    GetCompetitionTracks getTracks,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
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
            user.UserId, request.CompetitionId, DateTimeOffset.UtcNow, cancellationToken);
        if (visibility is null)
            return TypedResults.NotFound();
        if (visibility.DataScope == LeaderboardDataScope.Hidden)
        {
            return TypedResults.Ok(ScoreboardProtocolMapper.ToResponse(new ScoreboardSnapshot(
                request.CompetitionId, 0, 0, DateTimeOffset.UtcNow, null, [], [])
            {
                Visibility = visibility.Visibility,
                DataScope = LeaderboardDataScope.Hidden
            }));
        }

        var projection = visibility.DataScope == LeaderboardDataScope.Frozen
            ? await leaderboard.GetFrozenScoreboardAsync(request.CompetitionId, cancellationToken)
            : await leaderboard.GetScoreboardAsync(request.CompetitionId, cancellationToken);
        if (projection is not null)
        {
            var canObserve = user.UserId != Guid.Empty
                && await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, cancellationToken);
            var tracks = await getTracks.ExecuteAsync(
                request.CompetitionId,
                user.UserId == Guid.Empty ? null : user.UserId,
                canObserve,
                cancellationToken);
            if (tracks is null)
                return TypedResults.NotFound();
            projection = ScoreboardAudienceProjection.Filter(projection, canObserve);
            var snapshot = FilterSnapshot(projection.Snapshot, tracks, canObserve) with
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
                request.CompetitionId, visibility.VisibilityRevision, cancellationToken);
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

    private static ScoreboardSnapshot FilterSnapshot(
        ScoreboardSnapshot snapshot,
        CompetitionTracksView tracks,
        bool canObserve)
    {
        if (canObserve)
            return snapshot;
        var visibleKeys = tracks.Tracks
            .Where(track => !track.IsInternal && track.VisibleOnLeaderboard)
            .Select(track => track.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var viewerKeys = tracks.Tracks
            .Where(track => track.IsViewerTrack)
            .Select(track => track.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return snapshot with
        {
            Teams = snapshot.Teams.Where(team => visibleKeys.Contains(team.TrackKey)
                || viewerKeys.Contains(team.TrackKey) && team.TeamId == tracks.ViewerTeamId).ToArray(),
            Tracks = snapshot.Tracks.Where(track => visibleKeys.Contains(track.Key)
                || viewerKeys.Contains(track.Key)).ToArray()
        };
    }

    private Accepted<LeaderboardProcessingProtocolResponse> Processing(Guid competitionId)
    {
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{competitionId}/leaderboard";
        return TypedResults.Accepted(statusUrl, new LeaderboardProcessingProtocolResponse(
            competitionId, LeaderboardProjectionStateProtocol.Processing, statusUrl));
    }
}

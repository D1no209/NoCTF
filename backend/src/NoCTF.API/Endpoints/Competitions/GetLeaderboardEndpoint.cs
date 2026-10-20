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
using NoCTF.Domain.Competitions;
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
    Solve, Attack, Defense, Availability, Control, Penalty, BloodAward, Hint, ManualAdjustment, WriteUp
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardEntryKindProtocol>))]
public enum ScoreboardEntryKindProtocol
{
    Solve, Attack, Defense, Availability, Control, Penalty, BloodAward, Hint, ManualAdjustment, WriteUp
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardEntryOutcomeProtocol>))]
public enum ScoreboardEntryOutcomeProtocol { Pending, Succeeded, Failed, Rejected }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardAwardProtocol>))]
public enum ScoreboardAwardProtocol { FirstBlood, SecondBlood, ThirdBlood }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardAdjustmentKindProtocol>))]
public enum ScoreboardAdjustmentKindProtocol { ManualAdjustment, CompetitionPenalty, BanRecalculation, ScoreFloor }

public sealed record AwdScoreComponentResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdScoreComponent>))] AwdScoreComponent Component,
    long? Points, decimal? UnroundedPoints, decimal? RoundingDifference,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<NoCTF.Domain.Competitions.Awd.AwdSettlementState>))]
    NoCTF.Domain.Competitions.Awd.AwdSettlementState Settlement);

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

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardRoundStateProtocol>))]
public enum ScoreboardRoundStateProtocol { Pending, Running, Settling, Settled }

/// <summary>One round of the visible AWD/AWDP window; slots refer to it by its position in <c>rounds</c>.</summary>
public sealed record ScoreboardRoundResponse(
    Guid Id,
    int Number,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    ScoreboardRoundStateProtocol State);

/// <summary>
/// One published challenge in matrix order; slots, achievements and challenge totals refer to it
/// by its position in <c>challenges</c>.
/// </summary>
public sealed record ScoreboardChallengeResponse(
    Guid CompetitionChallengeId,
    long? CurrentScore);

/// <summary>Attempt and success counts of one scoring activity; points stay in slot detail.</summary>
public sealed record ScoreboardActivityResponse(
    ScoreboardBreakdownKindProtocol Kind,
    int SuccessfulCount,
    int AttemptCount);

/// <summary>
/// One sparse team × challenge (× round) cell. Entries, per-kind points and AWD components are
/// served by slot detail, so every publication only re-sends what the matrix renders.
/// </summary>
public sealed record ScoreboardSlotResponse(
    int Challenge,
    int? Round,
    ScoreboardScoreStateProtocol ScoreState,
    long? NetPoints,
    ScoreboardOperationStateProtocol OffenseState,
    ScoreboardOperationStateProtocol DefenseState,
    IReadOnlyList<ScoreboardActivityResponse> Activity,
    ScoreboardAwardProtocol? Award,
    long AwardPoints);

/// <summary>A team member who earned points or achieved a challenge; the only place a user identity appears.</summary>
public sealed record ScoreboardMemberResponse(
    Guid UserId,
    string DisplayName,
    long EarnedPoints);

/// <summary>First success of a team on a challenge; <c>Member</c> indexes the team's <c>members</c>.</summary>
public sealed record ScoreboardAchievementResponse(
    int Challenge,
    ScoreboardEntryKindProtocol Kind,
    int? Member,
    DateTimeOffset OccurredAt);

public sealed record ScoreboardWriteUpResponse(
    long DeductionPoints,
    int? DeductionPercent,
    DateTimeOffset UnlockedAt);

/// <summary>
/// Authoritative per-challenge total that the visible slots cannot express: AWD/AWDP totals over the
/// whole competition (and AWDP attack/defense), or a CTF/KoH challenge whose gross or net differs from
/// its slot (hint or manual deductions) or whose write-up was unlocked.
/// </summary>
public sealed record ScoreboardChallengeTotalResponse(
    int Challenge,
    long GrossPoints,
    long NetPoints,
    long? AttackScore,
    long? DefenseScore,
    ScoreboardWriteUpResponse? WriteUp);

public sealed record ScoreboardTeamResponse(
    Guid TeamId,
    string TeamName,
    string TrackKey,
    int? Rank,
    ScoreboardRankingStateProtocol RankingState,
    long TotalScore,
    long? AttackScore,
    long? DefenseScore,
    int GlobalAdjustmentCount,
    IReadOnlyList<ScoreboardMemberResponse> Members,
    IReadOnlyList<ScoreboardAchievementResponse>? Achievements,
    IReadOnlyList<ScoreboardChallengeTotalResponse> ChallengeTotals,
    IReadOnlyList<ScoreboardSlotResponse> Slots)
{
    public long TheoryScore { get; init; }
    public bool HasTheorySubmission { get; init; }
}

public sealed record ScoreboardTrackResponse(
    string Key,
    string Name,
    bool IsInternal,
    bool VisibleOnLeaderboard,
    bool IsViewerTrack);

/// <summary>
/// The complete leaderboard matrix: challenges and rounds define the columns, so no separate schema
/// request (or schema/snapshot coherence check) is needed. Titles stay in the challenge catalog.
/// </summary>
public sealed record ScoreboardSnapshotResponse(
    Guid CompetitionId,
    GameModeProtocol Mode,
    string Version,
    string ChallengeCatalogRevision,
    DateTimeOffset GeneratedAt,
    Guid? CurrentRoundId,
    IReadOnlyList<ScoreboardRoundResponse> Rounds,
    IReadOnlyList<ScoreboardChallengeResponse> Challenges,
    IReadOnlyList<ScoreboardTeamResponse> Teams)
{
    public bool TheoryEnabled { get; init; }
    public IReadOnlyList<ScoreboardTrackResponse> Tracks { get; init; } = [];
    public bool TracksEnabled { get; init; } = true;
    public LeaderboardVisibilityProtocol Visibility { get; init; }
    public LeaderboardDataScopeProtocol DataScope { get; init; }
    public DateTimeOffset? DataAsOf { get; init; }
    public int? RoundWindowStart { get; init; }
    public int? RoundWindowEnd { get; init; }
    public int? LatestRound { get; init; }
}

public sealed record LeaderboardProcessingProtocolResponse(
    Guid CompetitionId,
    LeaderboardProjectionStateProtocol State,
    string StatusUrl);

[Mapper]
internal static partial class ScoreboardProtocolMapper
{
    public static ScoreboardSnapshotResponse ToResponse(ScoreboardSchema schema, ScoreboardSnapshot value)
    {
        var challengeIds = schema.Columns.Select(column => column.CompetitionChallengeId).Distinct().ToArray();
        var challengeIndexes = challengeIds.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
        var roundIndexes = schema.Rounds.Select((round, index) => (round.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        var columns = schema.Columns.ToDictionary(column => column.Index, column => (
            Challenge: challengeIndexes[column.CompetitionChallengeId],
            Round: column.RoundId is Guid roundId ? roundIndexes[roundId] : (int?)null));
        var currentScores = value.CurrentChallengeScores.ToDictionary(score => score.CompetitionChallengeId, score => score.Score);
        var context = new ScoreboardResponseContext(schema.Mode, challengeIndexes, columns);
        return new(
            value.CompetitionId,
            CompetitionProtocolMapper.ToProtocol(schema.Mode),
            value.Version.ToString(CultureInfo.InvariantCulture),
            schema.ChallengeCatalogRevision.ToString(CultureInfo.InvariantCulture),
            value.GeneratedAt,
            value.CurrentRoundId,
            schema.Rounds.Select(round => new ScoreboardRoundResponse(
                round.Id, round.Number, round.StartAt, round.EndAt, ToProtocol(round.State))).ToArray(),
            challengeIds.Select(id => new ScoreboardChallengeResponse(id, currentScores.GetValueOrDefault(id))).ToArray(),
            value.Teams.Select(team => ToResponse(team, context)).ToArray())
        {
            Tracks = value.Tracks.Select(track => new ScoreboardTrackResponse(
                track.Key, track.Name, track.IsInternal, track.VisibleOnLeaderboard,
                track.IsViewerTrack)).ToArray(),
            TracksEnabled = value.TracksEnabled,
            TheoryEnabled = value.TheoryEnabled,
            Visibility = CompetitionProtocolMapper.ToProtocol(value.Visibility),
            DataScope = ToProtocol(value.DataScope),
            DataAsOf = value.DataAsOf,
            RoundWindowStart = schema.RoundWindowStart,
            RoundWindowEnd = schema.RoundWindowEnd,
            LatestRound = schema.LatestRound
        };
    }

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

    private sealed record ScoreboardResponseContext(
        GameMode Mode,
        IReadOnlyDictionary<Guid, int> Challenges,
        IReadOnlyDictionary<int, (int Challenge, int? Round)> Columns);

    private static ScoreboardTeamResponse ToResponse(ScoreboardTeam value, ScoreboardResponseContext context)
    {
        // Every identity appears once: contributors first, then achievers who earned no points.
        var members = value.MemberContributions
            .Select(item => new ScoreboardMemberResponse(item.UserId, item.DisplayName, item.EarnedPoints))
            .ToList();
        var memberIndexes = members.Select((member, index) => (member.UserId, index))
            .ToDictionary(pair => pair.UserId, pair => pair.index);
        int? Member(Guid? userId, string? displayName)
        {
            if (userId is not Guid id) return null;
            if (memberIndexes.TryGetValue(id, out var index)) return index;
            members.Add(new(id, string.IsNullOrWhiteSpace(displayName) ? "-" : displayName, 0));
            return memberIndexes[id] = members.Count - 1;
        }
        var achievements = value.Achievements?
            .Where(item => context.Challenges.ContainsKey(item.CompetitionChallengeId))
            .Select(item => new ScoreboardAchievementResponse(
                context.Challenges[item.CompetitionChallengeId],
                ToProtocol(item.Kind),
                Member(item.UserId, item.DisplayName),
                item.OccurredAt))
            .ToArray();
        return new(
            value.TeamId,
            value.TeamName,
            value.TrackKey,
            value.Rank,
            ToProtocol(value.RankingState),
            value.TotalScore,
            value.AttackScore,
            value.DefenseScore,
            value.GlobalAdjustmentCount,
            members,
            achievements,
            ChallengeTotals(value, context),
            value.Slots
                .Where(slot => context.Columns.ContainsKey(slot.ColumnIndex))
                .Select(slot =>
                {
                    var column = context.Columns[slot.ColumnIndex];
                    var award = slot.Entries.FirstOrDefault(entry => entry.Award is not null);
                    return new ScoreboardSlotResponse(
                        column.Challenge,
                        column.Round,
                        ToProtocol(slot.ScoreState),
                        slot.NetPoints,
                        ToProtocol(slot.OffenseState),
                        ToProtocol(slot.DefenseState),
                        slot.Breakdowns
                            .Where(item => item.AttemptCount != 0 || item.SuccessfulCount != 0)
                            .Select(item => new ScoreboardActivityResponse(
                                ToProtocol(item.Kind), item.SuccessfulCount, item.AttemptCount))
                            .ToArray(),
                        award?.Award is { } awardKind ? ToProtocol(awardKind) : null,
                        award?.AwardPoints ?? 0);
                })
                .ToArray())
        {
            TheoryScore = value.TheoryScore,
            HasTheorySubmission = value.HasTheorySubmission
        };
    }

    private static IReadOnlyList<ScoreboardChallengeTotalResponse> ChallengeTotals(
        ScoreboardTeam value, ScoreboardResponseContext context)
    {
        // CTF and KoH show the whole competition in one slot per challenge, whose net points normally
        // equal both the gross and the net benefit; a row is only sent when a write-up unlock, a hint or
        // manual deduction adds information. AWD/AWDP slots cover a round window, so their competition
        // totals are always sent.
        var windowed = context.Mode is GameMode.Awd or GameMode.Awdp;
        var slotNet = value.Slots
            .Where(slot => context.Columns.ContainsKey(slot.ColumnIndex))
            .GroupBy(slot => context.Columns[slot.ColumnIndex].Challenge)
            .ToDictionary(group => group.Key, group => group.Sum(slot => slot.NetPoints.GetValueOrDefault()));
        var benefits = value.ChallengeBenefits
            .Where(item => context.Challenges.ContainsKey(item.CompetitionChallengeId))
            .ToDictionary(item => item.CompetitionChallengeId);
        var scores = value.ChallengeScores
            .Where(item => context.Challenges.ContainsKey(item.CompetitionChallengeId))
            .ToDictionary(item => item.CompetitionChallengeId);
        return benefits.Keys.Union(scores.Keys)
            .Select(id => (Id: id, Benefit: benefits.GetValueOrDefault(id), Score: scores.GetValueOrDefault(id)))
            .Where(item => windowed
                || item.Benefit is { } benefit && (benefit.WriteUpUnlockedAt is not null
                    || benefit.NetPoints != slotNet.GetValueOrDefault(context.Challenges[item.Id])
                    || benefit.GrossPoints != slotNet.GetValueOrDefault(context.Challenges[item.Id])))
            .OrderBy(item => context.Challenges[item.Id])
            .Select(item => new ScoreboardChallengeTotalResponse(
                context.Challenges[item.Id],
                item.Benefit?.GrossPoints ?? checked(item.Score!.AttackScore + item.Score.DefenseScore),
                item.Benefit?.NetPoints ?? checked(item.Score!.AttackScore + item.Score.DefenseScore),
                item.Score?.AttackScore,
                item.Score?.DefenseScore,
                item.Benefit is { WriteUpUnlockedAt: { } unlockedAt } benefit
                    ? new ScoreboardWriteUpResponse(
                        benefit.WriteUpDeductionPoints, benefit.WriteUpDeductionPercent, unlockedAt)
                    : null))
            .ToArray();
    }

    public static AwdScoreComponentResponse ToResponse(AwdScoreComponentProjection value) =>
        new(value.Component, value.Points, value.UnroundedPoints, value.RoundingDifference, value.Settlement);

    public static LeaderboardDataScopeProtocol ToProtocol(LeaderboardDataScope value) => value switch
    {
        LeaderboardDataScope.Live => LeaderboardDataScopeProtocol.Live,
        LeaderboardDataScope.Frozen => LeaderboardDataScopeProtocol.Frozen,
        LeaderboardDataScope.Hidden => LeaderboardDataScopeProtocol.Hidden,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial ScoreboardRoundStateProtocol ToProtocol(ScoreboardRoundState value);

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
    ReadScoreboardAudience audiences,
    IBackendMessagePublisher messages,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<GetLeaderboardRequest, Results<Ok<ScoreboardSnapshotResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard");
        AllowAnonymous();
        Options(builder => builder.CacheOutput(LeaderboardOutputCachePolicy.Name));
        Summary(summary => summary.Summary = "Get the complete sparse scoreboard matrix or queue a refresh.");
    }

    public override async Task<Results<Ok<ScoreboardSnapshotResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetLeaderboardRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        // The output cache policy already resolved this viewer's audience to choose the shared cache entry.
        var audience = LeaderboardOutputCachePolicy.GetPrepared(HttpContext, request.CompetitionId)
            ?? await audiences.ExecuteAsync(request.CompetitionId, user.UserId, request.EndingRound, cancellationToken);
        switch (audience.State)
        {
            case ScoreboardAudienceState.NotFound:
                return TypedResults.NotFound();
            case ScoreboardAudienceState.Hidden:
                return TypedResults.Ok(ScoreboardProtocolMapper.ToResponse(
                    new ScoreboardSchema(request.CompetitionId, audience.Visibility!.GameMode, 0, 0, [], []),
                    new ScoreboardSnapshot(request.CompetitionId, 0, 0, timeProvider.GetUtcNow(), null, [], [])
                {
                    TracksEnabled = audience.Visibility!.TracksEnabled,
                    Visibility = audience.Visibility.Visibility,
                    DataScope = LeaderboardDataScope.Hidden
                }));
            case ScoreboardAudienceState.Ready:
                var visibility = audience.Visibility!;
                var projection = audience.Projection!;
                var snapshot = projection.Snapshot with
                {
                    Visibility = visibility.Visibility,
                    DataScope = visibility.DataScope,
                    DataAsOf = visibility.DataScope == LeaderboardDataScope.Frozen
                        ? projection.Snapshot.DataAsOf
                        : projection.Snapshot.GeneratedAt
                };
                LeaderboardOutputCachePolicy.SetResponseVersion(HttpContext, snapshot.Version);
                HttpContext.Response.Headers.ETag = LeaderboardOutputCachePolicy.EntityTag(
                    snapshot.Version, audience.CacheScope!, request.EndingRound);
                HttpContext.Response.Headers.CacheControl = "private, no-cache";
                return TypedResults.Ok(ScoreboardProtocolMapper.ToResponse(projection.Schema, snapshot));
        }

        if (audience.Visibility!.DataScope == LeaderboardDataScope.Frozen)
        {
            await messages.ApplyCompetitionVisibilityAsync(
                request.CompetitionId, timeProvider.GetUtcNow(), cancellationToken);
            return Processing(request.CompetitionId);
        }
        var status = await leaderboard.GetStatusAsync(request.CompetitionId, cancellationToken);
        if (status.LastFailureAt is not null)
        {
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: ApiMessages.Get(ApiMessageId.GetLeaderboardTitleLeaderboardProjectionUnavailable),
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = LeaderboardProblemCode.LeaderboardProjectionFailed
                });
        }
        await leaderboard.RequestRefreshAsync(request.CompetitionId, cancellationToken);
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

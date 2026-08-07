using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using System.Text.Json.Serialization;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Competitions;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardProblemCode>))]
internal enum LeaderboardProblemCode
{
    LeaderboardProjectionFailed
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardDataScopeProtocol>))]
public enum LeaderboardDataScopeProtocol { Live, Frozen, Hidden }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardProjectionStateProtocol>))]
public enum LeaderboardProjectionStateProtocol { Processing }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardSlotKindProtocol>))]
public enum LeaderboardSlotKindProtocol { Challenge, Service, Break, Fix, Control, Stage }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardBloodRankProtocol>))]
public enum LeaderboardBloodRankProtocol { First = 1, Second = 2, Third = 3 }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardPenaltyKindProtocol>))]
public enum LeaderboardPenaltyKindProtocol { WrongSubmission, HintUnlock }

public sealed record LeaderboardChallengeSummaryResponse(
    Guid CompetitionChallengeId,
    string Direction,
    int SolveCount);

public sealed record LeaderboardEntryResponse(
    int Rank,
    Guid TeamId,
    string TeamName,
    long Score,
    int SolveCount,
    DateTimeOffset? LastScoreAt,
    IReadOnlyList<LeaderboardChallengeSummaryResponse> Challenges);

public sealed record LeaderboardSlotSummaryResponse(
    string SlotKey,
    LeaderboardSlotKindProtocol Kind,
    string Label,
    int SuccessCount,
    DateTimeOffset? LastOccurredAt,
    LeaderboardBloodRankProtocol? BloodRank,
    DateTimeOffset? BloodAt);

public sealed record LeaderboardSubjectSummaryResponse(
    Guid SubjectId,
    string SubjectName,
    long Score,
    int SuccessCount,
    IReadOnlyList<LeaderboardSlotSummaryResponse> Slots);

public sealed record LeaderboardBloodSummaryResponse(
    string SlotKey,
    LeaderboardSlotKindProtocol SlotKind,
    LeaderboardBloodRankProtocol BloodRank,
    Guid TeamId,
    string TeamName,
    DateTimeOffset OccurredAt);

public sealed record LeaderboardScorePointResponse(DateTimeOffset At, long Score);

public sealed record LeaderboardSolveRecordResponse(
    Guid CompetitionChallengeId,
    DateTimeOffset At,
    long Points,
    int SolveOrdinal,
    string? SubmitterName);

public sealed record LeaderboardPenaltyRecordResponse(
    DateTimeOffset At,
    long Points,
    LeaderboardPenaltyKindProtocol Kind);

public sealed record LeaderboardTeamSeriesResponse(
    Guid TeamId,
    string TeamName,
    IReadOnlyList<LeaderboardScorePointResponse> Points)
{
    public IReadOnlyList<LeaderboardSolveRecordResponse> Solves { get; init; } = [];
    public IReadOnlyList<LeaderboardPenaltyRecordResponse> Penalties { get; init; } = [];
}

public sealed record LeaderboardChallengeInfoResponse(
    Guid CompetitionChallengeId,
    string Title,
    string Direction);

public sealed record LeaderboardProtocolResponse(
    Guid CompetitionId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<LeaderboardEntryResponse> Entries)
{
    public IReadOnlyList<LeaderboardSubjectSummaryResponse> Subjects { get; init; } = [];
    public IReadOnlyList<LeaderboardBloodSummaryResponse> Bloods { get; init; } = [];
    public IReadOnlyList<LeaderboardTeamSeriesResponse> Series { get; init; } = [];
    public IReadOnlyList<LeaderboardChallengeInfoResponse> Challenges { get; init; } = [];
    public long SnapshotRevision { get; init; }
    public long TargetRevision { get; init; }
    public bool Stale { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
    public LeaderboardVisibilityProtocol Visibility { get; init; }
    public LeaderboardDataScopeProtocol DataScope { get; init; }
    public DateTimeOffset? DataAsOf { get; init; }
}

public sealed record LeaderboardProcessingProtocolResponse(
    Guid CompetitionId,
    LeaderboardProjectionStateProtocol State,
    long TargetRevision,
    string StatusUrl);

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Source)]
internal static partial class LeaderboardProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial LeaderboardDataScopeProtocol ToProtocol(LeaderboardDataScope value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial LeaderboardProjectionStateProtocol ToProtocol(LeaderboardProjectionState value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial LeaderboardSlotKindProtocol ToProtocol(LeaderboardSlotKind value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial LeaderboardBloodRankProtocol ToProtocol(LeaderboardBloodRank value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial LeaderboardPenaltyKindProtocol ToProtocol(LeaderboardPenaltyKind value);

    private static LeaderboardVisibilityProtocol ToProtocol(
        NoCTF.Domain.Competitions.CompetitionLeaderboardVisibility value) =>
        CompetitionProtocolMapper.ToProtocol(value);

    public static partial LeaderboardProtocolResponse ToResponse(LeaderboardResponse value);

    public static partial LeaderboardProcessingProtocolResponse ToResponse(
        LeaderboardProcessingResponse value);
}

public sealed class GetLeaderboardRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class GetLeaderboardEndpoint(
    ILeaderboardCache leaderboard,
    IBackendMessagePublisher messages,
    ICompetitionVisibilityAccess access,
    IUserContext user)
    : Endpoint<GetLeaderboardRequest, Results<Ok<LeaderboardProtocolResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard");
        AllowAnonymous();
        Summary(s => s.Summary = "Get the cached leaderboard or queue an asynchronous refresh.");
    }

    public override async Task<Results<Ok<LeaderboardProtocolResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetLeaderboardRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var visibility = await access.ResolveAsync(
            user.UserId,
            request.CompetitionId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (visibility is null)
            return TypedResults.NotFound();
        if (visibility.DataScope == LeaderboardDataScope.Hidden)
        {
            return TypedResults.Ok(LeaderboardProtocolMapper.ToResponse(new LeaderboardResponse(
                request.CompetitionId,
                DateTimeOffset.UtcNow,
                [])
            {
                Visibility = visibility.Visibility,
                DataScope = LeaderboardDataScope.Hidden
            }));
        }
        var snapshot = visibility.DataScope == LeaderboardDataScope.Frozen
            ? await leaderboard.GetFrozenAsync(request.CompetitionId, cancellationToken)
            : await leaderboard.GetAsync(request.CompetitionId, cancellationToken);
        if (snapshot is not null)
        {
            if (visibility.DataScope == LeaderboardDataScope.Live && snapshot.Stale)
                await messages.ProjectLeaderboardAsync(request.CompetitionId, cancellationToken);
            return TypedResults.Ok(LeaderboardProtocolMapper.ToResponse(snapshot with
            {
                Visibility = visibility.Visibility,
                DataScope = visibility.DataScope,
                DataAsOf = visibility.DataScope == LeaderboardDataScope.Frozen
                    ? snapshot.DataAsOf
                    : snapshot.GeneratedAt
            }));
        }
        if (visibility.DataScope == LeaderboardDataScope.Frozen)
        {
            await messages.ApplyCompetitionVisibilityAsync(
                request.CompetitionId,
                visibility.VisibilityRevision,
                cancellationToken);
            return Processing(request.CompetitionId, visibility.LeaderboardRevision);
        }
        var status = await leaderboard.GetStatusAsync(request.CompetitionId, cancellationToken);
        if (status.LastFailureAt is not null)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Leaderboard projection is unavailable.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = LeaderboardProblemCode.LeaderboardProjectionFailed,
                    ["targetRevision"] = status.TargetRevision,
                    ["lastFailureAt"] = status.LastFailureAt
                });
        await messages.ProjectLeaderboardAsync(request.CompetitionId, cancellationToken);
        return Processing(request.CompetitionId, status.TargetRevision);
    }

    private Accepted<LeaderboardProcessingProtocolResponse> Processing(
        Guid competitionId,
        long targetRevision)
    {
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{competitionId}/leaderboard";
        return TypedResults.Accepted(statusUrl, LeaderboardProtocolMapper.ToResponse(new LeaderboardProcessingResponse(
            competitionId,
            LeaderboardProjectionState.Processing,
            targetRevision,
            statusUrl)));
    }
}

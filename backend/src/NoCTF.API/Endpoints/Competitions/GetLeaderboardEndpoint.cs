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

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardBloodRankProtocol>))]
public enum LeaderboardBloodRankProtocol { First = 1, Second = 2, Third = 3 }

public sealed record LeaderboardCellResponse(
    Guid CompetitionChallengeId,
    long Score,
    DateTimeOffset? SolvedAt,
    string? SolverName,
    LeaderboardBloodRankProtocol? BloodRank);

public sealed record LeaderboardEntryResponse(
    int Rank,
    Guid TeamId,
    string TeamName,
    long Score,
    int SolveCount,
    DateTimeOffset? LastScoreAt,
    IReadOnlyList<LeaderboardCellResponse> Cells);

public sealed record LeaderboardChallengeInfoResponse(
    Guid CompetitionChallengeId,
    string Title,
    string Direction,
    long? CurrentScore);

public sealed record LeaderboardProtocolResponse(
    Guid CompetitionId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<LeaderboardEntryResponse> Entries)
{
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
    private static partial LeaderboardBloodRankProtocol ToProtocol(LeaderboardBloodRank value);

    private static LeaderboardVisibilityProtocol ToProtocol(
        NoCTF.Domain.Competitions.CompetitionLeaderboardVisibility value) =>
        CompetitionProtocolMapper.ToProtocol(value);

    public static partial LeaderboardProtocolResponse ToResponse(LeaderboardResponse value);

    private static LeaderboardEntryResponse ToResponse(LeaderboardEntry value) => new(
        value.Rank,
        value.TeamId,
        value.TeamName,
        value.Score,
        value.SolveCount,
        value.LastScoreAt,
        value.Cells.Select(ToResponse).ToList());

    private static LeaderboardCellResponse ToResponse(LeaderboardCell value) => new(
        value.CompetitionChallengeId,
        value.Score,
        value.SolvedAt,
        value.SolverName,
        value.BloodRank is null ? null : ToProtocol(value.BloodRank.Value));

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

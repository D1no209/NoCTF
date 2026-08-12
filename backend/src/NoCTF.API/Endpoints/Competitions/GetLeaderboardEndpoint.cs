using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Teams.Moderation;
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
    string TrackKey,
    long Score,
    int SolveCount,
    DateTimeOffset? LastScoreAt,
    IReadOnlyList<LeaderboardCellResponse> Cells);

public sealed record LeaderboardChallengeInfoResponse(
    Guid CompetitionChallengeId,
    string Title,
    string Direction,
    long? CurrentScore);

public sealed record LeaderboardTrackInfoResponse(
    string Key,
    string Name,
    bool IsInternal,
    bool VisibleOnLeaderboard);

public sealed record LeaderboardProtocolResponse(
    Guid CompetitionId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<LeaderboardEntryResponse> Entries)
{
    public IReadOnlyList<LeaderboardChallengeInfoResponse> Challenges { get; init; } = [];
    public IReadOnlyList<LeaderboardTrackInfoResponse> Tracks { get; init; } = [];
    public LeaderboardVisibilityProtocol Visibility { get; init; }
    public LeaderboardDataScopeProtocol DataScope { get; init; }
    public DateTimeOffset? DataAsOf { get; init; }
}

public sealed record LeaderboardProcessingProtocolResponse(
    Guid CompetitionId,
    LeaderboardProjectionStateProtocol State,
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
        value.TrackKey,
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
    GetCompetitionTracks getTracks,
    ICompetitionModerationAuthorizer authorizer,
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
            var canObserve = user.UserId != Guid.Empty
                && await authorizer.CanObserveAsync(
                    user.UserId,
                    request.CompetitionId,
                    cancellationToken);
            var tracks = await getTracks.ExecuteAsync(
                request.CompetitionId,
                user.UserId == Guid.Empty ? null : user.UserId,
                canObserve,
                cancellationToken);
            if (tracks is null)
                return TypedResults.NotFound();
            var filteredSnapshot = FilterSnapshot(snapshot, tracks, canObserve);
            return TypedResults.Ok(LeaderboardProtocolMapper.ToResponse(filteredSnapshot with
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
            return Processing(request.CompetitionId);
        }
        var status = await leaderboard.GetStatusAsync(request.CompetitionId, cancellationToken);
        if (status.LastFailureAt is not null)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Leaderboard projection is unavailable.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = LeaderboardProblemCode.LeaderboardProjectionFailed
                });
        await leaderboard.InvalidateAsync(request.CompetitionId, cancellationToken);
        return Processing(request.CompetitionId);
    }

    private static LeaderboardResponse FilterSnapshot(
        LeaderboardResponse snapshot,
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
        var visibleEntries = snapshot.Entries
            .Where(entry => visibleKeys.Contains(entry.TrackKey)
                || viewerKeys.Contains(entry.TrackKey)
                    && entry.TeamId == tracks.ViewerTeamId)
            .ToArray();
        var visibleTrackInfo = snapshot.Tracks
            .Where(track => visibleKeys.Contains(track.Key)
                || viewerKeys.Contains(track.Key))
            .ToArray();
        return snapshot with { Entries = visibleEntries, Tracks = visibleTrackInfo };
    }

    private Accepted<LeaderboardProcessingProtocolResponse> Processing(
        Guid competitionId)
    {
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{competitionId}/leaderboard";
        return TypedResults.Accepted(statusUrl, LeaderboardProtocolMapper.ToResponse(new LeaderboardProcessingResponse(
            competitionId,
            LeaderboardProjectionState.Processing,
            statusUrl)));
    }
}

using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Scoring.Leaderboard;

public enum ScoreboardAudienceState
{
    NotFound,
    Hidden,
    Unavailable,
    Ready
}

/// <summary>
/// One viewer's projection of the published scoreboard. <see cref="CacheScope"/> identifies every
/// viewer that receives byte-identical scoreboard data for the same publication, so encoded
/// responses can be shared between them instead of being rebuilt per request.
/// </summary>
public sealed record ScoreboardAudience(
    ScoreboardAudienceState State,
    CompetitionVisibilityAccessDecision? Visibility = null,
    ScoreboardProjection? Projection = null,
    string? CacheScope = null);

public sealed class ReadScoreboardAudience(
    ILeaderboardCache leaderboard,
    ILeaderboardSnapshotFactory snapshots,
    ICompetitionVisibilityAccess access,
    GetCompetitionTracks getTracks,
    ICompetitionModerationAuthorizer authorizer,
    TimeProvider timeProvider)
{
    public async Task<ScoreboardAudience> ExecuteAsync(
        Guid competitionId,
        Guid userId,
        int? endingRound,
        CancellationToken cancellationToken)
    {
        var visibility = await access.ResolveAsync(
            userId, competitionId, timeProvider.GetUtcNow(), cancellationToken);
        if (visibility is null)
            return new(ScoreboardAudienceState.NotFound);
        if (visibility.DataScope == LeaderboardDataScope.Hidden)
            return new(ScoreboardAudienceState.Hidden, visibility);

        var projection = visibility.DataScope == LeaderboardDataScope.Frozen
            ? await leaderboard.GetFrozenScoreboardAsync(competitionId, cancellationToken)
            : await leaderboard.GetScoreboardAsync(competitionId, cancellationToken);
        if (projection is not null
            && visibility.DataScope != LeaderboardDataScope.Frozen
            && endingRound is int round
            && visibility.GameMode is GameMode.Awdp or GameMode.Awd)
        {
            var source = projection.Snapshot;
            var window = await snapshots.CreateScoreboardWindowAsync(
                competitionId,
                round,
                source.DataAsOf ?? source.GeneratedAt,
                cancellationToken);
            projection = window is null
                ? null
                : ScoreboardAudienceProjection.PreserveSnapshotScope(window, source);
        }
        if (projection is null)
            return new(ScoreboardAudienceState.Unavailable, visibility);

        var prefix = $"{visibility.Visibility}:{visibility.DataScope}";
        var requiresTrackScope = visibility.TracksEnabled || projection.Snapshot.TracksEnabled
            || projection.Snapshot.Tracks.Any(track => track.IsInternal);
        if (!requiresTrackScope)
        {
            return new(
                ScoreboardAudienceState.Ready,
                visibility,
                ScoreboardAudienceProjection.ForPublishedChallenges(projection),
                $"{prefix}:all");
        }

        var canViewInternalTracks = userId != Guid.Empty
            && await authorizer.CanJudgeAsync(userId, competitionId, cancellationToken);
        var tracks = await getTracks.ExecuteAsync(
            competitionId,
            userId == Guid.Empty ? null : userId,
            canViewInternalTracks,
            includeInvitationCodes: false,
            cancellationToken);
        if (tracks is null)
            return new(ScoreboardAudienceState.NotFound);
        projection = ScoreboardAudienceProjection.FilterTracks(
            ScoreboardAudienceProjection.ForPublishedChallenges(projection),
            tracks,
            canViewInternalTracks);
        return new(
            ScoreboardAudienceState.Ready,
            visibility,
            projection,
            canViewInternalTracks ? $"{prefix}:staff" : $"{prefix}:{ParticipantScope(tracks)}");
    }

    // Mirrors ScoreboardAudienceProjection.FilterTracks: participants share the public track set, and only
    // a viewer whose own track is not publicly listed additionally sees (and is keyed by) their own team.
    public static string ParticipantScope(CompetitionTracksView tracks)
    {
        var visible = tracks.Tracks
            .Where(track => !track.IsInternal && track.VisibleOnLeaderboard)
            .Select(track => track.Key.ToLowerInvariant())
            .Order(StringComparer.Ordinal)
            .ToArray();
        var viewerTrack = tracks.Tracks
            .Where(track => track.IsViewerTrack && !track.IsInternal)
            .Select(track => track.Key.ToLowerInvariant())
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        var viewerTeam = viewerTrack is not null && !visible.Contains(viewerTrack)
            ? tracks.ViewerTeamId?.ToString("N")
            : null;
        return $"public:{string.Join(',', visible)}:{viewerTrack}:{viewerTeam}";
    }
}

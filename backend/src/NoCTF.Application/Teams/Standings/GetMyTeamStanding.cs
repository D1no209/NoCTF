using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Application.Teams.Standings;

public enum MyTeamStandingState { Ready, Processing, NotFound, ProjectionFailed }
public sealed record MyTeamStanding(Guid TeamId, int? Rank, long? TotalScore,
    IReadOnlyList<ScoreboardChallengeBenefit> ChallengeBenefits, long Version,
    DateTimeOffset GeneratedAt, DateTimeOffset? DataAsOf,
    CompetitionLeaderboardVisibility Visibility, LeaderboardDataScope DataScope);
public sealed record MyTeamStandingResult(MyTeamStandingState State, MyTeamStanding? Standing = null);

public sealed record MyTeamStandingIdentity(Guid Id, bool IsBanned, TeamRegistrationStatus RegistrationStatus, string TrackKey);
public interface IMyTeamStandingIdentityReader
{
    Task<MyTeamStandingIdentity?> ReadAsync(Guid competitionId, Guid actorId, CancellationToken ct);
}

public sealed class GetMyTeamStanding(IMyTeamStandingIdentityReader teams, ICompetitionVisibilityAccess access,
    ILeaderboardCache cache, GetCompetitionTracks tracks, ICompetitionModerationAuthorizer authorizer,
    IBackendMessagePublisher messages, TimeProvider clock)
{
    public async Task<MyTeamStandingResult> ExecuteAsync(Guid competitionId, Guid actorId, CancellationToken ct)
    {
        var team = await teams.ReadAsync(competitionId, actorId, ct);
        if (team is null || team.IsBanned || team.RegistrationStatus != TeamRegistrationStatus.Approved)
            return new(MyTeamStandingState.NotFound);
        var permission = await access.ResolveAsync(actorId, competitionId, clock.GetUtcNow(), ct);
        if (permission is null || permission.GameMode == GameMode.LiveSolo)
            return new(MyTeamStandingState.NotFound);
        MyTeamStandingResult Hidden() => new(MyTeamStandingState.Ready,
            new(team.Id, null, null, [], 0, clock.GetUtcNow(), null,
                permission.Visibility, LeaderboardDataScope.Hidden));
        if (permission.DataScope == LeaderboardDataScope.Hidden) return Hidden();

        var projection = permission.DataScope == LeaderboardDataScope.Frozen
            ? await cache.GetFrozenScoreboardAsync(competitionId, ct)
            : await cache.GetScoreboardAsync(competitionId, ct);
        if (projection is null)
        {
            if (permission.DataScope == LeaderboardDataScope.Frozen)
                await messages.ApplyCompetitionVisibilityAsync(competitionId, clock.GetUtcNow(), ct);
            else
            {
                if ((await cache.GetStatusAsync(competitionId, ct)).LastFailureAt is not null)
                    return new(MyTeamStandingState.ProjectionFailed);
                await cache.RequestRefreshAsync(competitionId, ct);
            }
            return new(MyTeamStandingState.Processing);
        }
        var requiresTrackScope = permission.TracksEnabled || projection.Snapshot.TracksEnabled
            || projection.Snapshot.Tracks.Any(track => track.IsInternal);
        var internalTracks = requiresTrackScope && await authorizer.CanJudgeAsync(actorId, competitionId, ct);
        projection = ScoreboardAudienceProjection.ForPublishedChallenges(projection);
        if (requiresTrackScope)
        {
            var visibleTracks = await tracks.ExecuteAsync(competitionId, actorId, internalTracks, false, ct);
            if (visibleTracks is null) return new(MyTeamStandingState.NotFound);
            projection = ScoreboardAudienceProjection.FilterTracks(projection, visibleTracks, internalTracks);
        }
        var snapshot = projection.Snapshot;

        // A visibility or membership change while reading must not publish a superseded private result.
        var current = await access.ResolveAsync(actorId, competitionId, clock.GetUtcNow(), ct);
        var currentTeam = await teams.ReadAsync(competitionId, actorId, ct);
        if (current is null || currentTeam?.Id != team.Id || currentTeam.IsBanned
            || currentTeam.RegistrationStatus != TeamRegistrationStatus.Approved)
            return new(MyTeamStandingState.NotFound);
        if (current.DataScope == LeaderboardDataScope.Hidden)
            return new(MyTeamStandingState.Ready, new(team.Id, null, null, [], 0,
                clock.GetUtcNow(), null, current.Visibility, current.DataScope));
        if (current.DataScope != permission.DataScope || currentTeam.TrackKey != team.TrackKey
            || current.TracksEnabled != permission.TracksEnabled
            || requiresTrackScope && await authorizer.CanJudgeAsync(actorId, competitionId, ct) != internalTracks)
            return new(MyTeamStandingState.Processing);
        var row = snapshot.Teams.SingleOrDefault(item => item.TeamId == team.Id);
        return new(MyTeamStandingState.Ready, new(team.Id, row?.Rank, row?.TotalScore,
            row?.ChallengeBenefits ?? [], snapshot.Version, snapshot.GeneratedAt,
            permission.DataScope == LeaderboardDataScope.Frozen ? snapshot.DataAsOf : snapshot.GeneratedAt,
            permission.Visibility, permission.DataScope));
    }
}

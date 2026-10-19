using System.Globalization;
using System.Text;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Scoring.Leaderboard;

public enum SpectatorReadState { Ready, Processing, NotFound, CursorExpired }
public sealed record SpectatorScreenTeam(Guid Id, string Name, int? Rank, long Score);
public sealed record SpectatorSettlement(Guid RoundId, int Number, Guid TeamId,
    long Attack, long Lost, long Service, long Other);
public sealed record SpectatorScreen(long Version, DateTimeOffset DataAsOf, DateTimeOffset ServerTime,
    LeaderboardDataScope Scope, GameMode Mode, SpectatorFrame? Frame,
    IReadOnlyList<SpectatorScreenTeam> Teams, IReadOnlyList<ScoreboardChallengeCatalogItem> Challenges,
    ScoreboardRound? Round, IReadOnlyList<SpectatorSettlement> Settlements, string? NextCursor);
public sealed record SpectatorReadResult(SpectatorReadState State, SpectatorScreen? Screen = null);

public sealed class GetSpectatorScreen(ILeaderboardCache cache, ICompetitionVisibilityAccess access,
    GetCompetitionTracks tracks, IBackendMessagePublisher messages, TimeProvider clock)
{
    public async Task<SpectatorReadResult> ExecuteAsync(Guid id, GameMode mode, int minutes,
        string? cursor, CancellationToken ct, int limit = 256)
    {
        var now = clock.GetUtcNow();
        // Broadcasting never inherits the operator's privileged leaderboard scope.
        var permission = await access.ResolveAsync(Guid.Empty, id, now, ct);
        if (permission is null || permission.GameMode != mode) return new(SpectatorReadState.NotFound);
        if (permission.DataScope == LeaderboardDataScope.Hidden)
            return new(SpectatorReadState.Ready, new(0, now, now, permission.DataScope, mode, null, [], [], null, [], null));
        var projection = permission.DataScope == LeaderboardDataScope.Frozen
            ? await cache.GetFrozenSpectatorScoreboardAsync(id, ct) : await cache.GetScoreboardAsync(id, ct);
        if (projection?.Spectator is not { } frame)
        {
            if (permission.DataScope == LeaderboardDataScope.Frozen)
                await messages.ApplyCompetitionVisibilityAsync(id, now, ct);
            else await cache.RequestRefreshAsync(id, ct);
            return new(SpectatorReadState.Processing);
        }
        var publicTracks = await tracks.ExecuteAsync(id, null, false, false, ct);
        if (publicTracks is null) return new(SpectatorReadState.NotFound);
        if (permission.DataScope == LeaderboardDataScope.Frozen)
            publicTracks = publicTracks with
            {
                Tracks = publicTracks.Tracks.Where(track => projection.Snapshot.Tracks.Any(saved =>
                    saved.Key == track.Key && !saved.IsInternal && saved.VisibleOnLeaderboard)).ToArray()
            };
        var view = ScoreboardAudienceProjection.FilterTracks(
            ScoreboardAudienceProjection.ForPublishedChallenges(projection), publicTracks, false);
        var cutoff = projection.Snapshot.DataAsOf ?? projection.Snapshot.GeneratedAt;
        var version = projection.Snapshot.Version;
        var offset = 0;
        if (cursor is not null && !TryCursor(cursor, version, minutes, out offset))
            return new(SpectatorReadState.CursorExpired);
        frame = SpectatorAudience.Filter(frame, view.Snapshot.Teams.Select(x => x.TeamId).ToHashSet(),
            view.ChallengeCatalog.Challenges.Select(x => x.CompetitionChallengeId).ToHashSet());
        var history = frame.Changes.Where(x => x.At >= cutoff.AddMinutes(-minutes)).ToArray();
        var pageSize = Math.Clamp(limit, 1, 2000);
        var next = offset + pageSize < history.Length ? Cursor(version, minutes, offset + pageSize) : null;
        frame = frame with { Changes = history.Skip(offset).Take(pageSize).ToArray() };
        var settled = view.Schema.Rounds.Where(x => x.State == ScoreboardRoundState.Settled)
            .OrderByDescending(x => x.Number).Take(3).ToArray();
        var settlements = settled.SelectMany(round =>
        {
            var columns = view.Schema.Columns.Where(x => x.RoundId == round.Id).Select(x => x.Index).ToHashSet();
            return view.Snapshot.Teams.Select(team =>
            {
                var parts = team.Slots.Where(x => columns.Contains(x.ColumnIndex)).SelectMany(x => x.Breakdowns).ToArray();
                return new SpectatorSettlement(round.Id, round.Number, team.TeamId,
                    parts.Where(x => x.Kind == ScoreboardBreakdownKind.Attack).Sum(x => x.EarnedPoints),
                    parts.Where(x => x.Kind is ScoreboardBreakdownKind.Attack or ScoreboardBreakdownKind.Defense).Sum(x => x.DeductedPoints),
                    parts.Where(x => x.Kind == ScoreboardBreakdownKind.Availability).Sum(x => x.NetPoints),
                    parts.Where(x => x.Kind is not (ScoreboardBreakdownKind.Attack or ScoreboardBreakdownKind.Defense or ScoreboardBreakdownKind.Availability)).Sum(x => x.NetPoints));
            });
        }).ToArray();
        var currentPermission = await access.ResolveAsync(Guid.Empty, id, clock.GetUtcNow(), ct);
        if (currentPermission is null || currentPermission.GameMode != mode) return new(SpectatorReadState.NotFound);
        if (currentPermission.DataScope == LeaderboardDataScope.Hidden)
            return new(SpectatorReadState.Ready, new(0, now, now, LeaderboardDataScope.Hidden, mode, null, [], [], null, [], null));
        if (currentPermission.DataScope != permission.DataScope) return new(SpectatorReadState.Processing);
        return new(SpectatorReadState.Ready, new(version, cutoff, now, permission.DataScope, mode, frame,
            view.Snapshot.Teams.Select(x => new SpectatorScreenTeam(x.TeamId, x.TeamName, x.Rank, x.TotalScore)).ToArray(),
            view.ChallengeCatalog.Challenges,
            view.Schema.Rounds.FirstOrDefault(x => x.Id == view.Snapshot.CurrentRoundId), settlements, next));
    }

    private static string Cursor(long version, int minutes, int offset) => Convert.ToBase64String(
        Encoding.UTF8.GetBytes(FormattableString.Invariant($"{version}:{minutes}:{offset}")));

    private static bool TryCursor(string value, long version, int minutes, out int offset)
    {
        offset = 0;
        if (value.Length > 100) return false;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(value)).Split(':');
            return parts.Length == 3 && long.TryParse(parts[0], CultureInfo.InvariantCulture, out var parsedVersion)
                && parsedVersion == version && int.TryParse(parts[1], out var parsedMinutes) && parsedMinutes == minutes
                && int.TryParse(parts[2], out offset) && offset is >= 0 and <= 2000;
        }
        catch (FormatException) { return false; }
    }
}

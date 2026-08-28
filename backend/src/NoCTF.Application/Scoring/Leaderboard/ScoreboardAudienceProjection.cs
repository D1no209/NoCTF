using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Competitions.Tracks;

namespace NoCTF.Application.Scoring.Leaderboard;

public static class ScoreboardAudienceProjection
{
    public static ScoreboardProjection PreserveSnapshotScope(
        ScoreboardProjection projection,
        ScoreboardSnapshot source)
    {
        ScoreboardSnapshot Apply(ScoreboardSnapshot snapshot) => snapshot with
        {
            Visibility = source.Visibility,
            DataScope = source.DataScope,
            DataAsOf = source.DataAsOf
        };
        return projection with
        {
            Snapshot = Apply(projection.Snapshot),
            ParticipantView = projection.ParticipantView is { } participant
                ? participant with { Snapshot = Apply(participant.Snapshot) }
                : null
        };
    }

    public static ScoreboardProjection Filter(ScoreboardProjection projection, bool canObserve)
    {
        if (canObserve)
            return projection;
        if (projection.ParticipantView is { } participantView)
            return participantView.ToProjection();

        var catalogRevision = StableRevision([]);
        var schemaRevision = ScoreboardRevision.ForSchema(projection.Schema.Rounds, []);
        return projection with
        {
            ChallengeCatalog = projection.ChallengeCatalog with { Revision = catalogRevision, Challenges = [] },
            Schema = projection.Schema with
            {
                Revision = schemaRevision,
                ChallengeCatalogRevision = catalogRevision,
                Columns = []
            },
            Snapshot = projection.Snapshot with
            {
                SchemaRevision = schemaRevision,
                Actors = [],
                Teams = []
            },
            DetailActors = [],
            EntryAllocations = []
        };
    }

    public static ScoreboardProjection FilterTracks(
        ScoreboardProjection projection,
        CompetitionTracksView tracks,
        bool canViewInternalTracks)
    {
        if (canViewInternalTracks)
            return projection;
        var visibleKeys = tracks.Tracks
            .Where(track => !track.IsInternal && track.VisibleOnLeaderboard)
            .Select(track => track.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var viewerKeys = tracks.Tracks
            .Where(track => track.IsViewerTrack && !track.IsInternal)
            .Select(track => track.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var teams = projection.Snapshot.Teams
            .Where(team => visibleKeys.Contains(team.TrackKey)
                || viewerKeys.Contains(team.TrackKey) && team.TeamId == tracks.ViewerTeamId)
            .ToArray();
        var visibleTeamIds = teams.Select(team => team.TeamId).ToHashSet();
        Guid? VisibleTarget(Guid? teamId) => teamId is Guid value && visibleTeamIds.Contains(value)
            ? value
            : null;
        var actorIndexes = teams
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
        var actorIndexMap = actorPairs.ToDictionary(pair => pair.OldIndex, pair => pair.Actor.Index);
        int? MapActor(int? actorIndex) => actorIndex is int value
            && actorIndexMap.TryGetValue(value, out var mapped)
                ? mapped
                : null;
        return projection with
        {
            Snapshot = projection.Snapshot with
            {
                Actors = actorPairs.Select(pair => pair.Actor).ToArray(),
                Teams = teams.Select(team => team with
                {
                    GlobalAdjustments = team.GlobalAdjustments
                        .Select(item => item with { ActorIndex = MapActor(item.ActorIndex) })
                        .ToArray(),
                    Slots = team.Slots.Select(slot => slot with
                    {
                        Entries = slot.Entries.Select(entry => entry with
                        {
                            ActorIndex = MapActor(entry.ActorIndex),
                            TargetTeamId = VisibleTarget(entry.TargetTeamId)
                        }).ToArray()
                    }).ToArray()
                }).ToArray(),
                Tracks = projection.Snapshot.Tracks
                    .Where(track => visibleKeys.Contains(track.Key) || viewerKeys.Contains(track.Key))
                    .Select(track => track with { IsViewerTrack = viewerKeys.Contains(track.Key) })
                    .ToArray()
            },
            EntryAllocations = projection.EntryAllocations
                .Where(allocation => visibleTeamIds.Contains(allocation.TeamId))
                .Select(allocation => allocation with
                {
                    Entry = allocation.Entry with
                    {
                        TargetTeamId = VisibleTarget(allocation.Entry.TargetTeamId)
                    }
                })
                .ToArray()
        };
    }

    private static long StableRevision(IEnumerable<string> values)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values)));
        return Math.Max(1, BitConverter.ToInt64(bytes, 0) & long.MaxValue);
    }
}

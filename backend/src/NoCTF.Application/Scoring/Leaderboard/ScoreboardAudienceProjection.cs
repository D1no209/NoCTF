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
            // A historical window is a view of this published generation, not an older publication.
            Version = source.Version,
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

    public static ScoreboardProjection ForPublishedChallenges(ScoreboardProjection projection)
    {
        // Participant-facing leaderboard routes must not expose unpublished
        // challenges merely because the viewer also has staff permissions.
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

    public static ScoreboardProjection FilterCurrentAccess(ScoreboardProjection projection,
        IReadOnlySet<Guid> challengeIds, IReadOnlyDictionary<Guid, string> teamTracks)
    {
        var teamIds = teamTracks.Keys.ToHashSet();
        projection = ForPublishedChallenges(projection);
        var challenges = projection.ChallengeCatalog.Challenges.Where(item => challengeIds.Contains(item.CompetitionChallengeId)).ToArray();
        var columns = projection.Schema.Columns.Where(item => challengeIds.Contains(item.CompetitionChallengeId)).ToArray();
        var indexes = columns.Select(item => item.Index).ToHashSet();
        var catalogRevision = StableRevision(challenges.Select(item => $"{projection.ChallengeCatalog.Revision}:{item.CompetitionChallengeId:N}"));
        var schemaRevision = ScoreboardRevision.ForSchema(projection.Schema.Rounds, columns);
        var teams = projection.Snapshot.Teams.Where(team => teamIds.Contains(team.TeamId)).Select(team => team with
        {
            TrackKey = teamTracks[team.TeamId],
            Slots = team.Slots.Where(slot => indexes.Contains(slot.ColumnIndex)).ToArray(),
            ChallengeBenefits = team.ChallengeBenefits.Where(item => challengeIds.Contains(item.CompetitionChallengeId)).ToArray()
        }).ToArray();
        var actorIndexes = teams.SelectMany(team => team.GlobalAdjustments.Select(item => item.ActorIndex)
            .Concat(team.Slots.SelectMany(slot => slot.Entries.Select(entry => entry.ActorIndex))))
            .Where(index => index.HasValue).Select(index => index!.Value).ToHashSet();
        int? VisibleActor(int? index) => index is int value && actorIndexes.Contains(value) ? value : null;
        Guid? VisibleTeam(Guid? id) => id is Guid value && teamIds.Contains(value) ? value : null;
        return projection with
        {
            ChallengeCatalog = projection.ChallengeCatalog with { Revision = catalogRevision, Challenges = challenges },
            Schema = projection.Schema with { Revision = schemaRevision, ChallengeCatalogRevision = catalogRevision, Columns = columns },
            Snapshot = projection.Snapshot with
            {
                SchemaRevision = schemaRevision,
                Actors = projection.Snapshot.Actors.Where(actor => actorIndexes.Contains(actor.Index)).ToArray(),
                Teams = teams.Select(team => team with
                {
                    Slots = team.Slots.Select(slot => slot with { Entries = slot.Entries.Select(entry => entry with
                        { ActorIndex = VisibleActor(entry.ActorIndex), TargetTeamId = VisibleTeam(entry.TargetTeamId) }).ToArray() }).ToArray()
                }).ToArray(),
                CurrentChallengeScores = projection.Snapshot.CurrentChallengeScores.Where(item => challengeIds.Contains(item.CompetitionChallengeId)).ToArray()
            },
            EntryAllocations = projection.EntryAllocations.Where(item => teamIds.Contains(item.TeamId) && indexes.Contains(item.ColumnIndex)).ToArray(),
            ParticipantView = null
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

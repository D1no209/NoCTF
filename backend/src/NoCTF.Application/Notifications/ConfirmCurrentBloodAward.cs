using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Application.Notifications;

public sealed class ConfirmCurrentBloodAward(ILeaderboardSnapshotFactory projections, TimeProvider clock)
{
    public async Task<bool> ExecuteAsync(BloodAwarded message, CancellationToken ct)
    {
        var current = await projections.CreateScoreboardAsync(message.CompetitionId, clock.GetUtcNow(), ct);
        if (current is null) return false;
        var award = message.BloodRank switch
        {
            LeaderboardBloodRank.First => ScoreboardAward.FirstBlood,
            LeaderboardBloodRank.Second => ScoreboardAward.SecondBlood,
            LeaderboardBloodRank.Third => ScoreboardAward.ThirdBlood,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        var schema = current.ParticipantView?.Schema ?? current.Schema;
        var allocations = current.ParticipantView?.EntryAllocations ?? current.EntryAllocations;
        var columns = schema.Columns.Where(x => x.CompetitionChallengeId == message.CompetitionChallengeId)
            .Select(x => x.Index).ToHashSet();
        return allocations.Any(x => x.TeamId == message.TeamId && columns.Contains(x.ColumnIndex)
            && x.Entry.Award == award && x.Entry.OccurredAt == message.OccurredAt);
    }
}

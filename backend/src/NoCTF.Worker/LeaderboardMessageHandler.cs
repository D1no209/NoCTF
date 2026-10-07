using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker;

public sealed class LeaderboardMessageHandler(ILeaderboardCache leaderboard, NoCtfDbContext db, TimeProvider clock)
{
    public async Task Handle(
        ProjectLeaderboard message,
        CancellationToken cancellationToken)
    {
        await leaderboard.RefreshAsync(message.CompetitionId, cancellationToken);
        var projection = await leaderboard.GetScoreboardAsync(message.CompetitionId, cancellationToken);
        if (projection is null) return;
        var included = projection.Snapshot.Teams.SelectMany(team => team.ChallengeBenefits
            .Where(x => x.WriteUpUnlockedAt is not null).Select(x => (team.TeamId, x.CompetitionChallengeId, x.WriteUpUnlockedAt))).ToHashSet();
        var queued = await db.WriteUpUnlockReceipts.AsNoTracking().Where(x => x.CompetitionId == message.CompetitionId
            && db.GameplayFacts.Any(f => f.Id == x.GameplayFactId && f.State == GameplayFactState.Queued))
            .OrderBy(x => x.UnlockedAt).ThenBy(x => x.GameplayFactId).Take(256)
            .Select(x => new { x.GameplayFactId, x.TeamId, x.CompetitionChallengeId, x.UnlockedAt }).ToArrayAsync(cancellationToken);
        var ids = queued.Where(x => included.Contains((x.TeamId, x.CompetitionChallengeId, (DateTimeOffset?)x.UnlockedAt)))
            .Select(x => x.GameplayFactId).ToArray();
        if (ids.Length == 0) return;
        var now = clock.GetUtcNow(); var stamp = Guid.NewGuid();
        await db.GameplayFacts.Where(x => ids.Contains(x.Id) && x.State == GameplayFactState.Queued)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.State, GameplayFactState.Completed)
                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.ConcurrencyStamp, stamp), cancellationToken);
    }
}

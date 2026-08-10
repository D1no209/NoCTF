using Microsoft.EntityFrameworkCore;

namespace NoCTF.Infrastructure.Persistence;

public static class LeaderboardDirty
{
    public static async Task MarkAsync(
        NoCtfDbContext db,
        Guid competitionId,
        CancellationToken ct)
    {
        var tracked = db.Competitions.Local.FirstOrDefault(item => item.Id == competitionId);
        if (tracked is not null)
        {
            tracked.LeaderboardDirty = true;
            return;
        }

        var changed = await db.Competitions
            .Where(competition => competition.Id == competitionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(competition => competition.LeaderboardDirty, true), ct);
        if (changed != 1)
            throw new InvalidOperationException("Competition disappeared while its leaderboard was marked dirty.");
    }
}

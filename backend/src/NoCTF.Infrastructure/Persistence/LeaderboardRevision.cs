using Microsoft.EntityFrameworkCore;

namespace NoCTF.Infrastructure.Persistence;

public static class LeaderboardRevision
{
    public static async Task IncrementAsync(
        NoCtfDbContext db,
        Guid competitionId,
        CancellationToken ct)
    {
        var changed = await db.Competitions
            .Where(competition => competition.Id == competitionId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    competition => competition.LeaderboardRevision,
                    competition => competition.LeaderboardRevision + 1),
                ct);
        if (changed != 1)
            throw new InvalidOperationException("Competition disappeared while its leaderboard revision was being updated.");
    }
}

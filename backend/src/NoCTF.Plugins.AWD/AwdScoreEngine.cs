using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.AWD;

/// <summary>
/// Calculates round scores for all teams based on service health and attack records.
/// Writes a ScoreEvent per team and refreshes the leaderboard.
/// </summary>
public class AwdScoreEngine(
    ApplicationDbContext db,
    ILeaderboardService leaderboardService,
    IHubNotifierService hubNotifier,
    ILogger<AwdScoreEngine> logger)
{
    public async Task CalculateRoundScoreAsync(Guid competitionId, int roundNumber, CancellationToken ct = default)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == competitionId, ct);

        var teams = await db.Teams
            .IgnoreQueryFilters()
            .Where(t => t.CompetitionId == competitionId)
            .ToListAsync(ct);

        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == competitionId)
            .ToListAsync(ct);

        foreach (var team in teams)
        {
            int roundScoreDelta = 0;

            foreach (var challenge in challenges)
            {
                // Service health
                var check = await db.AwdCheckResults
                    .IgnoreQueryFilters()
                    .Where(r => r.CompetitionId == competitionId
                             && r.TeamId == team.Id
                             && r.ChallengeId == challenge.Id
                             && r.RoundNumber == roundNumber)
                    .OrderByDescending(r => r.CheckedAt)
                    .FirstOrDefaultAsync(ct);

                if (check?.Status == AwdCheckStatus.Healthy)
                    roundScoreDelta += competition.ServiceOnlinePoints ?? 100;
                else if (check?.Status == AwdCheckStatus.Down)
                    roundScoreDelta -= competition.ServiceDownPenalty ?? 50;

                // Been attacked penalty (any unique attacker this round for this team+challenge)
                var wasAttacked = await db.AwdAttackRecords
                    .IgnoreQueryFilters()
                    .AnyAsync(a => a.CompetitionId == competitionId
                               && a.VictimTeamId == team.Id
                               && a.ChallengeId == challenge.Id
                               && a.RoundNumber == roundNumber, ct);

                if (wasAttacked)
                    roundScoreDelta -= competition.BeenAttackedPenalty ?? 50;
            }

            if (roundScoreDelta != 0)
            {
                db.ScoreEvents.Add(new ScoreEvent
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competitionId,
                    TeamId = team.Id,
                    EventType = "awd_round_score",
                    PointsDelta = roundScoreDelta,
                    Reason = $"Round {roundNumber} score",
                    Timestamp = DateTime.UtcNow,
                    RoundNumber = roundNumber
                });
            }
        }

        await db.SaveChangesAsync(ct);

        // Refresh leaderboard and push snapshot
        try
        {
            var entries = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
            var payload = entries.Select(e => new LeaderboardEntryPayload(e.Rank, e.TeamId, e.TeamName, e.TotalScore, e.SolvedCount));
            await hubNotifier.NotifyLeaderboardSnapshotAsync(competitionId, payload, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push leaderboard snapshot after round {Round} scoring.", roundNumber);
        }
    }
}

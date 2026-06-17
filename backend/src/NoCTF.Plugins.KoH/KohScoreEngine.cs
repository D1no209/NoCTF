using Microsoft.EntityFrameworkCore;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.KoH;

/// <summary>
/// Handles KoH scoring logic: updating control records and awarding points per interval.
/// Extracted for testability.
/// </summary>
public class KohScoreEngine(
    ApplicationDbContext db,
    ILeaderboardService leaderboard,
    IHubNotifierService hubNotifier)
{
    /// <summary>
    /// Updates the control record for a challenge based on the current controller.
    /// Awards points if the same team holds control, or transitions to a new controller.
    /// </summary>
    public async Task UpdateControlAsync(
        Guid competitionId,
        Guid challengeId,
        Guid? newControllerTeamId,
        DateTime now,
        int controlPointsPerInterval,
        CancellationToken ct = default)
    {
        var activeRecord = await db.KohControlRecords
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId
                     && r.ChallengeId == challengeId
                     && r.EndTime == null)
            .OrderByDescending(r => r.StartTime)
            .FirstOrDefaultAsync(ct);

        if (activeRecord?.TeamId == newControllerTeamId)
        {
            // Same controller — award points for this interval
            if (newControllerTeamId.HasValue)
            {
                db.ScoreEvents.Add(new ScoreEvent
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competitionId,
                    TeamId = newControllerTeamId.Value,
                    ChallengeId = challengeId,
                    EventType = "koh_control",
                    PointsDelta = controlPointsPerInterval,
                    Reason = $"Controlled challenge {challengeId} for one poll interval",
                    Timestamp = now
                });
                await db.SaveChangesAsync(ct);
                await leaderboard.CalculateLeaderboardAsync(competitionId, ct);
            }
        }
        else
        {
            // Controller changed — end old record, start new one
            if (activeRecord is not null)
                activeRecord.EndTime = now;

            if (newControllerTeamId.HasValue)
            {
                db.KohControlRecords.Add(new KohControlRecord
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competitionId,
                    ChallengeId = challengeId,
                    TeamId = newControllerTeamId.Value,
                    StartTime = now,
                    EndTime = null
                });
            }

            await db.SaveChangesAsync(ct);
            await hubNotifier.NotifyKohUpdateAsync(competitionId, challengeId, newControllerTeamId, now, ct);
            await leaderboard.CalculateLeaderboardAsync(competitionId, ct);
        }
    }
}

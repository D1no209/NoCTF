using Microsoft.EntityFrameworkCore;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.KoH;

/// <summary>
/// Handles KoH control-record tracking and emits control-held scoring signals.
/// Extracted for testability.
/// </summary>
public class KohScoreEngine(
    ApplicationDbContext db,
    ILeaderboardService leaderboard,
    IRedisLeaderboardCache leaderboardCache,
    IHubNotifierService hubNotifier,
    IScoreSignalEmitter scoreSignalEmitter)
{
    /// <summary>
    /// Updates the control record for a challenge based on the current controller.
    /// Emits a scoring signal if the same team holds control, or transitions to a new controller.
    /// </summary>
    public async Task UpdateControlAsync(
        Guid competitionId,
        Guid challengeId,
        Guid? newControllerTeamId,
        DateTime now,
        int controlPointsPerInterval,
        CancellationToken ct = default)
    {
        if (newControllerTeamId.HasValue)
        {
            var isActiveTeam = await db.Teams
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(t =>
                    t.Id == newControllerTeamId.Value &&
                    t.CompetitionId == competitionId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned, ct);
            if (!isActiveTeam)
                newControllerTeamId = null;
        }

        var activeRecord = await db.KohControlRecords
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId
                     && r.ChallengeId == challengeId
                     && r.EndTime == null)
            .OrderByDescending(r => r.StartTime)
            .FirstOrDefaultAsync(ct);

        if (activeRecord?.TeamId == newControllerTeamId)
        {
            // Same controller — emit a scoring fact for this interval.
            if (newControllerTeamId.HasValue)
            {
                await scoreSignalEmitter.EmitAsync(new ScoreSignalCreate(
                    CompetitionId: competitionId,
                    TeamId: newControllerTeamId.Value,
                    SignalType: ScoreSignalTypes.ControlHeld,
                    IdempotencyKey: $"koh:{challengeId:N}:{newControllerTeamId.Value:N}:{now:yyyyMMddHHmmss}",
                    SubjectType: "challenge",
                    SubjectId: challengeId,
                    PayloadJson: ScoringJson.Serialize(new { source = "poll" }),
                    OccurredAt: now), ct);
                var entries = await leaderboard.CalculateLeaderboardAsync(competitionId, ct);
                await leaderboardCache.UpdateAsync(competitionId, entries, ct);
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
            var entries = await leaderboard.CalculateLeaderboardAsync(competitionId, ct);
            await leaderboardCache.UpdateAsync(competitionId, entries, ct);
        }
    }
}

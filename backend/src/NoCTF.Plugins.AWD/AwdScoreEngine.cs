using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.AWD;

/// <summary>
/// Emits round scoring signals for all teams based on service health and attack records.
/// Scoring strategies convert those facts into ScoreEvents.
/// </summary>
public class AwdScoreEngine(
    ApplicationDbContext db,
    ILeaderboardService leaderboardService,
    IHubNotifierService hubNotifier,
    IScoreSignalEmitter scoreSignalEmitter,
    ILogger<AwdScoreEngine> logger)
{
    public async Task CalculateRoundScoreAsync(Guid competitionId, int roundNumber, CancellationToken ct = default)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == competitionId, ct);

        var teams = await db.Teams
            .IgnoreQueryFilters()
            .Where(t =>
                t.CompetitionId == competitionId &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned)
            .ToListAsync(ct);

        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == competitionId)
            .ToListAsync(ct);

        foreach (var team in teams)
        {
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
                {
                    await scoreSignalEmitter.EmitAsync(new ScoreSignalCreate(
                        CompetitionId: competitionId,
                        TeamId: team.Id,
                        SignalType: ScoreSignalTypes.ServiceCheckPassed,
                        IdempotencyKey: $"awd:{roundNumber}:{team.Id:N}:{challenge.Id:N}:service:passed",
                        SubjectType: "challenge",
                        SubjectId: challenge.Id,
                        RoundNumber: roundNumber,
                        OccurredAt: check.CheckedAt), ct);
                }
                else if (check?.Status == AwdCheckStatus.Down)
                {
                    await scoreSignalEmitter.EmitAsync(new ScoreSignalCreate(
                        CompetitionId: competitionId,
                        TeamId: team.Id,
                        SignalType: ScoreSignalTypes.ServiceCheckFailed,
                        IdempotencyKey: $"awd:{roundNumber}:{team.Id:N}:{challenge.Id:N}:service:failed",
                        SubjectType: "challenge",
                        SubjectId: challenge.Id,
                        RoundNumber: roundNumber,
                        OccurredAt: check.CheckedAt), ct);
                }

                // Been attacked penalty (any unique attacker this round for this team+challenge)
                var wasAttacked = await db.AwdAttackRecords
                    .IgnoreQueryFilters()
                    .AnyAsync(a => a.CompetitionId == competitionId
                               && a.VictimTeamId == team.Id
                               && a.ChallengeId == challenge.Id
                               && a.RoundNumber == roundNumber, ct);

                if (wasAttacked)
                {
                    await scoreSignalEmitter.EmitAsync(new ScoreSignalCreate(
                        CompetitionId: competitionId,
                        TeamId: team.Id,
                        SignalType: ScoreSignalTypes.ServiceAttacked,
                        IdempotencyKey: $"awd:{roundNumber}:{team.Id:N}:{challenge.Id:N}:been-attacked",
                        SubjectType: "challenge",
                        SubjectId: challenge.Id,
                        RoundNumber: roundNumber), ct);
                }
            }
        }

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

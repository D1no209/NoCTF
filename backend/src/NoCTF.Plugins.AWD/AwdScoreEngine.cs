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
    IRedisLeaderboardCache leaderboardCache,
    IHubNotifierService hubNotifier,
    IScoreSignalEmitter scoreSignalEmitter,
    ILogger<AwdScoreEngine> logger)
{
    public async Task CalculateRoundScoreAsync(Guid competitionId, int roundNumber, CancellationToken ct = default)
    {
        var teams = await db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t =>
                t.CompetitionId == competitionId &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.CompetitionId == competitionId && !c.IsDeleting)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var activeTeamIds = teams.ToHashSet();
        var activeChallengeIds = challenges.ToHashSet();
        var checkRows = await db.AwdCheckResults
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(result =>
                result.CompetitionId == competitionId &&
                result.RoundNumber == roundNumber &&
                activeTeamIds.Contains(result.TeamId) &&
                activeChallengeIds.Contains(result.ChallengeId))
            .Select(result => new
            {
                result.TeamId,
                result.ChallengeId,
                result.Status,
                result.CheckedAt
            })
            .ToListAsync(ct);
        var latestChecks = checkRows
            .GroupBy(result => (result.TeamId, result.ChallengeId))
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(result => result.CheckedAt).First());
        var attacks = await db.AwdAttackRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(attack =>
                attack.CompetitionId == competitionId &&
                attack.RoundNumber == roundNumber &&
                activeTeamIds.Contains(attack.VictimTeamId) &&
                activeChallengeIds.Contains(attack.ChallengeId))
            .Select(attack => new
            {
                attack.AttackerTeamId,
                attack.VictimTeamId,
                attack.ChallengeId,
                attack.Timestamp
            })
            .ToListAsync(ct);
        var attackedPairs = attacks
            .Select(attack => (TeamId: attack.VictimTeamId, attack.ChallengeId))
            .ToHashSet();

        var signals = new List<ScoreSignalCreate>(teams.Count * challenges.Count * 2 + attacks.Count);

        // Attack records are the authoritative acceptance facts. Re-emitting
        // their deterministic keys makes round scoring a projection rebuilder
        // as well as a calculator, repairing a missing signal/event after an
        // interrupted or ambiguously committed API request.
        foreach (var attack in attacks.Where(attack => activeTeamIds.Contains(attack.AttackerTeamId)))
        {
            signals.Add(new ScoreSignalCreate(
                CompetitionId: competitionId,
                TeamId: attack.AttackerTeamId,
                SignalType: ScoreSignalTypes.AttackAccepted,
                IdempotencyKey: $"awd:{roundNumber}:{attack.AttackerTeamId:N}:{attack.VictimTeamId:N}:{attack.ChallengeId:N}:attack",
                SubjectType: "challenge",
                SubjectId: attack.ChallengeId,
                RoundNumber: roundNumber,
                PayloadJson: ScoringJson.Serialize(new { victimTeamId = attack.VictimTeamId }),
                OccurredAt: attack.Timestamp));
        }

        foreach (var team in teams)
        {
            foreach (var challenge in challenges)
            {
                latestChecks.TryGetValue((team, challenge), out var check);

                if (check?.Status == AwdCheckStatus.Healthy)
                {
                    signals.Add(new ScoreSignalCreate(
                        CompetitionId: competitionId,
                        TeamId: team,
                        SignalType: ScoreSignalTypes.ServiceCheckPassed,
                        IdempotencyKey: $"awd:{roundNumber}:{team:N}:{challenge:N}:service:passed",
                        SubjectType: "challenge",
                        SubjectId: challenge,
                        RoundNumber: roundNumber,
                        OccurredAt: check.CheckedAt));
                }
                else if (check?.Status == AwdCheckStatus.Down)
                {
                    signals.Add(new ScoreSignalCreate(
                        CompetitionId: competitionId,
                        TeamId: team,
                        SignalType: ScoreSignalTypes.ServiceCheckFailed,
                        IdempotencyKey: $"awd:{roundNumber}:{team:N}:{challenge:N}:service:failed",
                        SubjectType: "challenge",
                        SubjectId: challenge,
                        RoundNumber: roundNumber,
                        OccurredAt: check.CheckedAt));
                }

                if (attackedPairs.Contains((team, challenge)))
                {
                    signals.Add(new ScoreSignalCreate(
                        CompetitionId: competitionId,
                        TeamId: team,
                        SignalType: ScoreSignalTypes.ServiceAttacked,
                        IdempotencyKey: $"awd:{roundNumber}:{team:N}:{challenge:N}:been-attacked",
                        SubjectType: "challenge",
                        SubjectId: challenge,
                        RoundNumber: roundNumber));
                }
            }
        }

        if (signals.Count > 0)
            await scoreSignalEmitter.EmitBatchAsync(signals, ct);

        // Refresh leaderboard and push snapshot
        try
        {
            var cacheVersion = await leaderboardCache.ReserveUpdateVersionAsync(competitionId, ct);
            var entries = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
            await leaderboardCache.UpdateAsync(competitionId, entries, cacheVersion, ct);
            var payload = entries.Select(e => new LeaderboardEntryPayload(e.Rank, e.TeamId, e.TeamName, e.TotalScore, e.SolvedCount));
            await hubNotifier.NotifyLeaderboardSnapshotAsync(competitionId, payload, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push leaderboard snapshot after round {Round} scoring.", roundNumber);
        }
    }
}

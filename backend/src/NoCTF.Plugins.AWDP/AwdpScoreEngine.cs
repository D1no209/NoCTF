using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.AWDP;

public class AwdpScoreEngine(
    ApplicationDbContext db,
    IScoreEventWriter scoreEventWriter,
    ILeaderboardService leaderboardService,
    IHubNotifierService hubNotifier,
    AwdpConfigResolver configResolver,
    ILogger<AwdpScoreEngine> logger)
{
    public async Task CalculateRoundScoreAsync(Guid competitionId, int roundNumber, CancellationToken ct = default)
    {
        var round = await db.AwdpRounds
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.CompetitionId == competitionId &&
                r.RoundNumber == roundNumber, ct);
        var roundStart = round?.StartTime ?? DateTime.MinValue;
        var teams = await db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t =>
                t.CompetitionId == competitionId &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned)
            .ToListAsync(ct);

        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.CompetitionId == competitionId)
            .ToListAsync(ct);

        var states = await db.AwdpTeamChallengeStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.CompetitionId == competitionId)
            .ToListAsync(ct);

        var stateMap = states.ToDictionary(s => (s.TeamId, s.ChallengeId));
        var breakSuccessCounts = states
            .Where(s => s.BreakStatus == AwdpBreakStatus.BreakSuccess)
            .GroupBy(s => s.ChallengeId)
            .ToDictionary(g => g.Key, g => g.Count());
        var fixSuccessCounts = states
            .Where(s => s.FixStatus == AwdpFixStatus.FixSuccess)
            .GroupBy(s => s.ChallengeId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var team in teams)
        {
            foreach (var challenge in challenges)
            {
                var exists = await db.AwdpRoundScores
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(s =>
                        s.CompetitionId == competitionId &&
                        s.RoundNumber == roundNumber &&
                        s.TeamId == team.Id &&
                        s.ChallengeId == challenge.Id, ct);

                if (exists)
                    continue;

                stateMap.TryGetValue((team.Id, challenge.Id), out var state);
                var config = await configResolver.ResolveAsync(competitionId, challenge.Id, ct);
                var attackEffective = state?.BreakStatus == AwdpBreakStatus.BreakSuccess &&
                                      IsEffectiveForRound(state.BreakSucceededAt, roundStart);
                var defenseEffective = state?.FixStatus == AwdpFixStatus.FixSuccess &&
                                       IsEffectiveForRound(state.FixSucceededAt, roundStart);
                var breakSuccessCount = breakSuccessCounts.GetValueOrDefault(challenge.Id);
                var fixSuccessCount = fixSuccessCounts.GetValueOrDefault(challenge.Id);
                var attackScorePerRound = ScoreDecayCalculator.CalculatePerRoundPoints(
                    breakSuccessCount,
                    config.AttackScorePerRound,
                    challenge.PointsConfig,
                    challenge.DifficultyCoefficient);
                var defenseScorePerRound = ScoreDecayCalculator.CalculatePerRoundPoints(
                    fixSuccessCount,
                    config.DefenseScorePerRound,
                    challenge.PointsConfig,
                    challenge.DifficultyCoefficient);
                var attackDelta = attackEffective
                    ? attackScorePerRound
                    : 0;
                var defenseDelta = defenseEffective
                    ? defenseScorePerRound
                    : 0;
                var penaltyDelta = 0;
                var reasons = new List<string>();

                if (attackDelta > 0)
                    reasons.Add("break_success");
                else if (state?.BreakStatus == AwdpBreakStatus.BreakSuccess)
                    reasons.Add("break_success_next_round");

                if (defenseDelta > 0)
                    reasons.Add("fix_success");
                else if (state?.FixStatus == AwdpFixStatus.FixSuccess)
                    reasons.Add("fix_success_next_round");

                if (state?.ServiceStatus == AwdpServiceStatus.ServiceError && config.ServicePenaltyEnabled)
                {
                    penaltyDelta += config.ServicePenaltyPerRound;
                    reasons.Add("service_error_penalty");
                }

                if (state is not null && IsViolationStatus(state.FixStatus) && config.ViolationPenaltyEnabled)
                {
                    penaltyDelta += config.ViolationPenalty;
                    reasons.Add("violation_penalty");
                }

                if (state?.FixStatus == AwdpFixStatus.FixFailed)
                    reasons.Add("fix_failed_no_penalty");

                var roundDelta = attackDelta + defenseDelta - penaltyDelta;
                var roundScore = new AwdpRoundScore
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competitionId,
                    TeamId = team.Id,
                    ChallengeId = challenge.Id,
                    RoundNumber = roundNumber,
                    AttackScoreDelta = attackDelta,
                    DefenseScoreDelta = defenseDelta,
                    PenaltyDelta = penaltyDelta,
                    RoundScoreDelta = roundDelta,
                    Reason = reasons.Count == 0 ? "no_delta" : string.Join(",", reasons),
                    CreatedAt = DateTime.UtcNow
                };

                db.AwdpRoundScores.Add(roundScore);
                await db.SaveChangesAsync(ct);

                await scoreEventWriter.WriteAsync(new ScoreEventCreate(
                    CompetitionId: competitionId,
                    TeamId: team.Id,
                    ScoringKey: ScoringKeys.AwdpRound,
                    EventType: "awdp.round",
                    PointsDelta: roundDelta,
                    IdempotencyKey: $"awdp:round:{roundNumber}:{team.Id:N}:{challenge.Id:N}",
                    ChallengeId: challenge.Id,
                    Reason: roundScore.Reason,
                    RoundNumber: roundNumber,
                    MetadataJson: ScoringJson.Serialize(new
                    {
                        attackDelta,
                        defenseDelta,
                        penaltyDelta,
                        breakSuccessCount,
                        fixSuccessCount,
                        breakStatus = state?.BreakStatus.ToString() ?? AwdpBreakStatus.BreakNotStarted.ToString(),
                        fixStatus = state?.FixStatus.ToString() ?? AwdpFixStatus.FixNotStarted.ToString(),
                        serviceStatus = state?.ServiceStatus.ToString() ?? AwdpServiceStatus.ServiceUnknown.ToString()
                    }),
                    Timestamp: roundScore.CreatedAt), ct);
            }
        }

        await PushLeaderboardAsync(competitionId, roundNumber, ct);
    }

    private async Task PushLeaderboardAsync(Guid competitionId, int roundNumber, CancellationToken ct)
    {
        try
        {
            var entries = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
            var payload = entries.Select(e => new LeaderboardEntryPayload(
                e.Rank,
                e.TeamId,
                e.TeamName,
                e.TotalScore,
                e.SolvedCount));
            await hubNotifier.NotifyLeaderboardSnapshotAsync(competitionId, payload, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push AWDP leaderboard snapshot after round {Round}.", roundNumber);
        }
    }

    private static bool IsViolationStatus(AwdpFixStatus status)
        => status is AwdpFixStatus.AuditFailed
            or AwdpFixStatus.FixScriptError
            or AwdpFixStatus.FixTimeout;

    private static bool IsEffectiveForRound(DateTime? succeededAt, DateTime roundStart)
        => succeededAt is null || succeededAt <= roundStart;
}

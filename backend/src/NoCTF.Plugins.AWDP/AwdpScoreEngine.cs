using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.AWDP;

public class AwdpScoreEngine(
    ApplicationDbContext db,
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    IHubNotifierService hubNotifier,
    ILogger<AwdpScoreEngine> logger)
{
    public async Task CalculateRoundScoreAsync(Guid competitionId, int roundNumber, CancellationToken ct = default)
    {
        var round = await db.AwdpRounds
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(r =>
                r.CompetitionId == competitionId &&
                r.RoundNumber == roundNumber, ct);
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(c => c.Id == competitionId, ct);
        var roundStart = round.StartTime;
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
            .ToListAsync(ct);
        var activeTeamIds = teams.ToHashSet();
        var activeChallengeIds = challenges.Select(challenge => challenge.Id).ToHashSet();

        var states = await db.AwdpTeamChallengeStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                activeTeamIds.Contains(s.TeamId) &&
                activeChallengeIds.Contains(s.ChallengeId))
            .ToListAsync(ct);

        var existingRoundScores = await db.AwdpRoundScores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(score =>
                score.CompetitionId == competitionId &&
                score.RoundNumber == roundNumber &&
                activeTeamIds.Contains(score.TeamId) &&
                activeChallengeIds.Contains(score.ChallengeId))
            .ToListAsync(ct);
        var existingScoreMap = existingRoundScores.ToDictionary(score => (score.TeamId, score.ChallengeId));
        var existingEventKeys = (await db.ScoreEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(scoreEvent =>
                    scoreEvent.CompetitionId == competitionId &&
                    scoreEvent.RoundNumber == roundNumber &&
                    scoreEvent.ScoringKey == ScoringKeys.AwdpRound)
                .Select(scoreEvent => scoreEvent.IdempotencyKey)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var stateMap = states.ToDictionary(s => (s.TeamId, s.ChallengeId));
        var breakSuccessCounts = states
            .Where(s =>
                s.BreakStatus == AwdpBreakStatus.BreakSuccess &&
                IsEffectiveForRound(s.BreakSucceededAt, roundStart))
            .GroupBy(s => s.ChallengeId)
            .ToDictionary(g => g.Key, g => g.Count());
        var fixSuccessCounts = states
            .Where(s =>
                s.FixStatus == AwdpFixStatus.FixSuccess &&
                IsEffectiveForRound(s.FixSucceededAt, roundStart))
            .GroupBy(s => s.ChallengeId)
            .ToDictionary(g => g.Key, g => g.Count());
        var scoringConfigByChallenge = challenges.ToDictionary(
            challenge => challenge.Id,
            challenge =>
            {
                var config = AwdpConfigResolver.Resolve(competition, challenge);
                return new ChallengeRoundScoring(
                    config,
                    ScoreDecayCalculator.CalculatePerRoundPoints(
                        breakSuccessCounts.GetValueOrDefault(challenge.Id),
                        config.AttackScorePerRound,
                        challenge.PointsConfig,
                        challenge.DifficultyCoefficient),
                    ScoreDecayCalculator.CalculatePerRoundPoints(
                        fixSuccessCounts.GetValueOrDefault(challenge.Id),
                        config.DefenseScorePerRound,
                        challenge.PointsConfig,
                        challenge.DifficultyCoefficient));
            });

        var now = DateTime.UtcNow;
        var newRoundScores = new List<AwdpRoundScore>();
        var newScoreEvents = new List<ScoreEvent>();

        foreach (var team in teams)
        {
            foreach (var challenge in challenges)
            {
                var idempotencyKey = $"awdp:round:{roundNumber}:{team:N}:{challenge.Id:N}";
                if (existingScoreMap.TryGetValue((team, challenge.Id), out var existingRoundScore))
                {
                    if (existingRoundScore.RoundScoreDelta != 0 && existingEventKeys.Add(idempotencyKey))
                    {
                        newScoreEvents.Add(BuildScoreEvent(
                            existingRoundScore,
                            idempotencyKey,
                            ScoringJson.Serialize(new
                            {
                                existingRoundScore.AttackScoreDelta,
                                existingRoundScore.DefenseScoreDelta,
                                existingRoundScore.PenaltyDelta,
                                restored = true
                            })));
                    }
                    continue;
                }

                stateMap.TryGetValue((team, challenge.Id), out var state);
                var scoring = scoringConfigByChallenge[challenge.Id];
                var config = scoring.Config;
                var attackEffective = state?.BreakStatus == AwdpBreakStatus.BreakSuccess &&
                                      IsEffectiveForRound(state.BreakSucceededAt, roundStart);
                var defenseEffective = state?.FixStatus == AwdpFixStatus.FixSuccess &&
                                       IsEffectiveForRound(state.FixSucceededAt, roundStart);
                var breakSuccessCount = breakSuccessCounts.GetValueOrDefault(challenge.Id);
                var fixSuccessCount = fixSuccessCounts.GetValueOrDefault(challenge.Id);
                var attackDelta = attackEffective
                    ? scoring.AttackScorePerRound
                    : 0;
                var defenseDelta = defenseEffective
                    ? scoring.DefenseScorePerRound
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
                    TeamId = team,
                    ChallengeId = challenge.Id,
                    RoundNumber = roundNumber,
                    AttackScoreDelta = attackDelta,
                    DefenseScoreDelta = defenseDelta,
                    PenaltyDelta = penaltyDelta,
                    RoundScoreDelta = roundDelta,
                    Reason = reasons.Count == 0 ? "no_delta" : string.Join(",", reasons),
                    CreatedAt = now
                };

                newRoundScores.Add(roundScore);
                existingScoreMap[(team, challenge.Id)] = roundScore;
                if (roundDelta != 0 && existingEventKeys.Add(idempotencyKey))
                {
                    newScoreEvents.Add(BuildScoreEvent(
                        roundScore,
                        idempotencyKey,
                        ScoringJson.Serialize(new
                        {
                            attackDelta,
                            defenseDelta,
                            penaltyDelta,
                            breakSuccessCount,
                            fixSuccessCount,
                            breakStatus = state?.BreakStatus.ToString() ?? AwdpBreakStatus.BreakNotStarted.ToString(),
                            fixStatus = state?.FixStatus.ToString() ?? AwdpFixStatus.FixNotStarted.ToString(),
                            serviceStatus = state?.ServiceStatus.ToString() ?? AwdpServiceStatus.ServiceUnknown.ToString()
                        })));
                }
            }
        }

        if (newRoundScores.Count > 0 || newScoreEvents.Count > 0)
        {
            await using var transaction = await BeginTransactionIfSupportedAsync(ct);
            db.AwdpRoundScores.AddRange(newRoundScores);
            db.ScoreEvents.AddRange(newScoreEvents);
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }

        await PushLeaderboardAsync(competitionId, roundNumber, ct);
    }

    private async Task PushLeaderboardAsync(Guid competitionId, int roundNumber, CancellationToken ct)
    {
        try
        {
            var cacheVersion = await leaderboardCache.ReserveUpdateVersionAsync(competitionId, ct);
            var entries = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
            await leaderboardCache.UpdateAsync(competitionId, entries, cacheVersion, ct);
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

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(CancellationToken ct)
        => db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

    private static ScoreEvent BuildScoreEvent(
        AwdpRoundScore roundScore,
        string idempotencyKey,
        string metadataJson)
        => new()
        {
            Id = Guid.NewGuid(),
            CompetitionId = roundScore.CompetitionId,
            TeamId = roundScore.TeamId,
            ChallengeId = roundScore.ChallengeId,
            ScoringKey = ScoringKeys.AwdpRound,
            EventType = "awdp.round",
            PointsDelta = roundScore.RoundScoreDelta,
            IdempotencyKey = idempotencyKey,
            Reason = roundScore.Reason,
            RoundNumber = roundScore.RoundNumber,
            MetadataJson = metadataJson,
            Timestamp = roundScore.CreatedAt
        };

    private static bool IsViolationStatus(AwdpFixStatus status)
        => status is AwdpFixStatus.AuditFailed
            or AwdpFixStatus.FixScriptError
            or AwdpFixStatus.FixTimeout
            or AwdpFixStatus.FixRuleViolation;

    private static bool IsEffectiveForRound(DateTime? succeededAt, DateTime roundStart)
        => succeededAt is null || succeededAt <= roundStart;

    private sealed record ChallengeRoundScoring(
        AwdpChallengeConfig Config,
        int AttackScorePerRound,
        int DefenseScorePerRound);
}

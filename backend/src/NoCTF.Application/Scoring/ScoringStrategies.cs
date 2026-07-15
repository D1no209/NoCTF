using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Application.Scoring;

public class DecaySolveScoringStrategy(ApplicationDbContext db, IScoreEventWriter writer) : IScoringStrategy
{
    public string ScoringKey => ScoringKeys.DecaySolve;

    public bool CanHandle(ScoreSignal signal)
        => signal.SignalType == ScoreSignalTypes.SolveAccepted && signal.SubjectId.HasValue;

    public async Task HandleAsync(ScoreSignal signal, CancellationToken ct = default)
    {
        var challengeId = signal.SubjectId!.Value;
        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Id == challengeId &&
                c.CompetitionId == signal.CompetitionId &&
                !c.IsDeleting,
                ct);
        if (challenge is null)
            return;

        var solves = await CtfSolveRanking.GetRankedSolvesAsync(db, signal.CompetitionId, challengeId, ct);

        if (solves.Count == 0)
            return;

        var currentPoints = CtfScoreCalculator.CalculateChallengePoints(
            solves.Count,
            challenge.PointsConfig,
            challenge.DifficultyCoefficient);

        var solveTeamIds = solves.Select(solve => solve.TeamId).ToList();
        var currentTotals = await db.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e =>
                e.CompetitionId == signal.CompetitionId &&
                solveTeamIds.Contains(e.TeamId) &&
                e.ChallengeId == challengeId &&
                e.ScoringKey == ScoringKey)
            .GroupBy(e => e.TeamId)
            .Select(group => new
            {
                TeamId = group.Key,
                Total = group.Sum(e => (long)e.PointsDelta)
            })
            .ToDictionaryAsync(row => row.TeamId, row => row.Total, ct);
        var scoreEvents = new List<ScoreEventCreate>(solves.Count);

        foreach (var solve in solves)
        {
            var currentTotal = currentTotals.GetValueOrDefault(solve.TeamId);
            var delta = checked(currentPoints - (int)currentTotal);
            var eventType = currentTotal == 0 ? "ctf.solve" : "ctf.score-adjustment";
            var suffix = currentTotal == 0
                ? $"solve:{currentPoints}"
                : $"correction:{solves.Count}:{currentPoints}";

            scoreEvents.Add(new ScoreEventCreate(
                CompetitionId: signal.CompetitionId,
                TeamId: solve.TeamId,
                ScoringKey: ScoringKey,
                EventType: eventType,
                PointsDelta: delta,
                IdempotencyKey: $"ctf:{solve.TeamId:N}:{challengeId:N}:{suffix}",
                ChallengeId: challengeId,
                SourceSignalId: signal.Id,
                Reason: currentTotal == 0
                    ? $"Solved challenge '{challenge.Title}'"
                    : $"Dynamic score adjustment for challenge '{challenge.Title}'",
                MetadataJson: ScoringJson.Serialize(new
                {
                    solveCount = solves.Count,
                    currentPoints,
                    solveRank = solve.Rank,
                    decayFunction = challenge.PointsConfig.DecayFunction,
                    difficultyCoefficient = challenge.DifficultyCoefficient
                }),
                Timestamp: signal.OccurredAt));
        }

        await writer.WriteBatchAsync(scoreEvents, ct);
    }
}

public class BloodBonusScoringStrategy(ApplicationDbContext db, IScoreEventWriter writer) : IScoringStrategy
{
    public string ScoringKey => ScoringKeys.BloodBonus;

    public bool CanHandle(ScoreSignal signal)
        => signal.SignalType == ScoreSignalTypes.SolveAccepted && signal.SubjectId.HasValue;

    public async Task HandleAsync(ScoreSignal signal, CancellationToken ct = default)
    {
        var challengeId = signal.SubjectId!.Value;
        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Id == challengeId &&
                c.CompetitionId == signal.CompetitionId &&
                !c.IsDeleting,
                ct);
        if (challenge is null)
            return;

        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == signal.CompetitionId, ct);
        if (competition is null)
            return;

        var solves = await CtfSolveRanking.GetRankedSolvesAsync(db, signal.CompetitionId, challengeId, ct);
        if (solves.Count == 0)
            return;

        var currentPoints = CtfScoreCalculator.CalculateChallengePoints(
            solves.Count,
            challenge.PointsConfig,
            challenge.DifficultyCoefficient);

        var bloodSolves = solves.Where(solve => solve.Rank <= 3).ToList();
        var bloodTeamIds = bloodSolves.Select(solve => solve.TeamId).ToList();
        var currentTotals = await db.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e =>
                e.CompetitionId == signal.CompetitionId &&
                bloodTeamIds.Contains(e.TeamId) &&
                e.ChallengeId == challengeId &&
                e.ScoringKey == ScoringKey)
            .GroupBy(e => e.TeamId)
            .Select(group => new
            {
                TeamId = group.Key,
                Total = group.Sum(e => (long)e.PointsDelta)
            })
            .ToDictionaryAsync(row => row.TeamId, row => row.Total, ct);
        var scoreEvents = new List<ScoreEventCreate>(bloodSolves.Count);

        foreach (var solve in bloodSolves)
        {
            var expectedBonus = CtfScoreCalculator.CalculateBloodBonus(
                solve.Rank,
                currentPoints,
                competition,
                challenge.EnableBloodBonus);

            var currentTotal = currentTotals.GetValueOrDefault(solve.TeamId);
            var delta = checked(expectedBonus - (int)currentTotal);
            scoreEvents.Add(new ScoreEventCreate(
                CompetitionId: signal.CompetitionId,
                TeamId: solve.TeamId,
                ScoringKey: ScoringKey,
                EventType: currentTotal == 0 ? "ctf.blood-bonus" : "ctf.blood-bonus-adjustment",
                PointsDelta: delta,
                IdempotencyKey: $"ctf-blood:{solve.TeamId:N}:{challengeId:N}:correction:{solves.Count}:{expectedBonus}",
                ChallengeId: challengeId,
                SourceSignalId: signal.Id,
                Reason: solve.Rank switch
                {
                    1 => $"First blood bonus for challenge '{challenge.Title}'",
                    2 => $"Second blood bonus for challenge '{challenge.Title}'",
                    _ => $"Third blood bonus for challenge '{challenge.Title}'"
                },
                MetadataJson: ScoringJson.Serialize(new
                {
                    solveCount = solves.Count,
                    solveRank = solve.Rank,
                    currentPoints,
                    bonusPoints = expectedBonus
                }),
                Timestamp: signal.OccurredAt));
        }


        await writer.WriteBatchAsync(scoreEvents, ct);
    }
}

file sealed record RankedSolve(Guid TeamId, DateTime SubmittedAt, int Rank);

file static class CtfSolveRanking
{
    public static async Task<List<RankedSolve>> GetRankedSolvesAsync(
        ApplicationDbContext db,
        Guid competitionId,
        Guid challengeId,
        CancellationToken ct)
    {
        var solves = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                s.ChallengeId == challengeId &&
                s.IsCorrect)
            .Join(
                db.Teams.IgnoreQueryFilters().AsNoTracking().Where(t =>
                    t.CompetitionId == competitionId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned),
                s => s.TeamId,
                t => t.Id,
                (s, _) => new { s.TeamId, s.SubmittedAt })
            .GroupBy(s => s.TeamId)
            .Select(g => new { TeamId = g.Key, SubmittedAt = g.Min(s => s.SubmittedAt) })
            .OrderBy(s => s.SubmittedAt)
            .ToListAsync(ct);

        return solves.Select((solve, index) => new RankedSolve(solve.TeamId, solve.SubmittedAt, index + 1)).ToList();
    }
}

public class RoundAccumulationScoringStrategy(ApplicationDbContext db, IScoreEventWriter writer) : IBatchScoringStrategy
{
    public string ScoringKey => ScoringKeys.RoundAccumulation;

    public bool CanHandle(ScoreSignal signal)
        => signal.SignalType is ScoreSignalTypes.AttackAccepted
            or ScoreSignalTypes.ServiceCheckPassed
            or ScoreSignalTypes.ServiceCheckFailed
            or ScoreSignalTypes.ServiceAttacked;

    public Task HandleAsync(ScoreSignal signal, CancellationToken ct = default)
        => HandleBatchAsync([signal], ct);

    public async Task HandleBatchAsync(IReadOnlyCollection<ScoreSignal> signals, CancellationToken ct = default)
    {
        var matchingSignals = signals.Where(CanHandle).ToList();
        if (matchingSignals.Count == 0)
            return;

        var scoreEvents = new List<ScoreEventCreate>(matchingSignals.Count);
        foreach (var competitionGroup in matchingSignals.GroupBy(signal => signal.CompetitionId))
        {
            var competition = await db.Competitions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == competitionGroup.Key, ct);
            if (competition is null)
                continue;

            var competitionSignals = competitionGroup.ToList();
            var challengeIds = competitionSignals
                .Where(signal => signal.SubjectType == "challenge" && signal.SubjectId.HasValue)
                .Select(signal => signal.SubjectId!.Value)
                .Distinct()
                .ToList();
            var challengeMap = await db.Challenges
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(challenge =>
                    challenge.CompetitionId == competitionGroup.Key &&
                    challengeIds.Contains(challenge.Id) &&
                    !challenge.IsDeleting)
                .ToDictionaryAsync(challenge => challenge.Id, ct);

            // All AWD score facts are challenge-scoped. A fact whose challenge
            // was removed (or is being removed) must remain durable for audit,
            // but it must not recreate a score projection after cleanup.
            competitionSignals = competitionSignals
                .Where(signal =>
                    signal.SubjectType == "challenge" &&
                    signal.SubjectId.HasValue &&
                    challengeMap.ContainsKey(signal.SubjectId.Value))
                .ToList();
            if (competitionSignals.Count == 0)
                continue;

            challengeIds = challengeMap.Keys.ToList();

            var attackSignals = competitionSignals
                .Where(signal => signal.SignalType == ScoreSignalTypes.AttackAccepted)
                .ToList();
            var serviceSignals = competitionSignals
                .Where(signal => signal.SignalType == ScoreSignalTypes.ServiceCheckPassed)
                .ToList();
            var attackCounts = await LoadAttackSuccessCountsAsync(
                competitionGroup.Key,
                attackSignals,
                challengeIds,
                ct);
            var serviceCounts = await LoadServiceSuccessCountsAsync(
                competitionGroup.Key,
                serviceSignals,
                challengeIds,
                ct);

            foreach (var signal in competitionSignals)
            {
                var challengeId = signal.SubjectId!.Value;
                var challenge = challengeMap[challengeId];
                var successCount = signal.SignalType switch
                {
                    ScoreSignalTypes.AttackAccepted =>
                        attackCounts.GetValueOrDefault((challengeId, signal.RoundNumber)),
                    ScoreSignalTypes.ServiceCheckPassed =>
                        serviceCounts.GetValueOrDefault((challengeId, signal.RoundNumber)),
                    _ => 0
                };
                var points = signal.SignalType switch
                {
                    ScoreSignalTypes.AttackAccepted => competition.AttackPoints ?? 50,
                    ScoreSignalTypes.ServiceCheckPassed => competition.ServiceOnlinePoints ?? 100,
                    ScoreSignalTypes.ServiceCheckFailed => -(competition.ServiceDownPenalty ?? 50),
                    ScoreSignalTypes.ServiceAttacked => -(competition.BeenAttackedPenalty ?? 50),
                    _ => 0
                };

                if (points > 0)
                {
                    points = ScoreDecayCalculator.CalculatePerRoundPoints(
                        successCount,
                        points,
                        challenge.PointsConfig,
                        challenge.DifficultyCoefficient);
                }

                scoreEvents.Add(new ScoreEventCreate(
                    CompetitionId: signal.CompetitionId,
                    TeamId: signal.TeamId,
                    ScoringKey: ScoringKey,
                    EventType: signal.SignalType switch
                    {
                        ScoreSignalTypes.AttackAccepted => "awd.attack",
                        ScoreSignalTypes.ServiceCheckPassed => "awd.service-online",
                        ScoreSignalTypes.ServiceCheckFailed => "awd.service-down",
                        ScoreSignalTypes.ServiceAttacked => "awd.been-attacked",
                        _ => "awd.round"
                    },
                    PointsDelta: points,
                    IdempotencyKey: $"round:{signal.IdempotencyKey}",
                    ChallengeId: challengeId,
                    SourceSignalId: signal.Id,
                    Reason: signal.SignalType,
                    RoundNumber: signal.RoundNumber,
                    Timestamp: signal.OccurredAt));
            }
        }

        await writer.WriteBatchAsync(scoreEvents, ct);
    }

    private async Task<Dictionary<(Guid ChallengeId, int? RoundNumber), int>> LoadAttackSuccessCountsAsync(
        Guid competitionId,
        IReadOnlyCollection<ScoreSignal> signals,
        IReadOnlyCollection<Guid> challengeIds,
        CancellationToken ct)
    {
        if (signals.Count == 0 || challengeIds.Count == 0)
            return [];

        var includeAllRounds = signals.Any(signal => !signal.RoundNumber.HasValue);
        var rounds = signals.Where(signal => signal.RoundNumber.HasValue)
            .Select(signal => signal.RoundNumber!.Value)
            .Distinct()
            .ToList();
        var rows = await db.AwdAttackRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(attack =>
                attack.CompetitionId == competitionId &&
                challengeIds.Contains(attack.ChallengeId) &&
                (includeAllRounds || rounds.Contains(attack.RoundNumber)))
            .Join(
                db.Teams.IgnoreQueryFilters().AsNoTracking().Where(team =>
                    team.CompetitionId == competitionId &&
                    team.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !team.IsBanned),
                attack => attack.AttackerTeamId,
                team => team.Id,
                (attack, _) => new { attack.ChallengeId, attack.RoundNumber, attack.AttackerTeamId })
            .Distinct()
            .ToListAsync(ct);

        var result = rows
            .GroupBy(row => (row.ChallengeId, RoundNumber: (int?)row.RoundNumber))
            .ToDictionary(group => group.Key, group => group.Count());
        if (includeAllRounds)
        {
            foreach (var group in rows.GroupBy(row => row.ChallengeId))
                result[(group.Key, null)] = group.Select(row => row.AttackerTeamId).Distinct().Count();
        }
        return result;
    }

    private async Task<Dictionary<(Guid ChallengeId, int? RoundNumber), int>> LoadServiceSuccessCountsAsync(
        Guid competitionId,
        IReadOnlyCollection<ScoreSignal> signals,
        IReadOnlyCollection<Guid> challengeIds,
        CancellationToken ct)
    {
        if (signals.Count == 0 || challengeIds.Count == 0)
            return [];

        var rounds = signals.Where(signal => signal.RoundNumber.HasValue)
            .Select(signal => signal.RoundNumber!.Value)
            .Distinct()
            .ToList();
        if (rounds.Count == 0)
            return [];
        var rows = await db.AwdCheckResults
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(result =>
                result.CompetitionId == competitionId &&
                result.Status == AwdCheckStatus.Healthy &&
                challengeIds.Contains(result.ChallengeId) &&
                rounds.Contains(result.RoundNumber))
            .Join(
                db.Teams.IgnoreQueryFilters().AsNoTracking().Where(team =>
                    team.CompetitionId == competitionId &&
                    team.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !team.IsBanned),
                result => result.TeamId,
                team => team.Id,
                (result, _) => new { result.ChallengeId, result.RoundNumber, result.TeamId })
            .Distinct()
            .ToListAsync(ct);

        var result = rows
            .GroupBy(row => (row.ChallengeId, RoundNumber: (int?)row.RoundNumber))
            .ToDictionary(group => group.Key, group => group.Count());
        return result;
    }
}

public class OneShotVerificationScoringStrategy(ApplicationDbContext db, IScoreEventWriter writer) : IScoringStrategy
{
    public string ScoringKey => ScoringKeys.OneShotVerification;

    public bool CanHandle(ScoreSignal signal)
        => signal.SignalType == ScoreSignalTypes.PatchVerified;

    public async Task HandleAsync(ScoreSignal signal, CancellationToken ct = default)
    {
        if (signal.SubjectType != "challenge" || !signal.SubjectId.HasValue)
            return;

        var challengeExists = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(challenge =>
                challenge.Id == signal.SubjectId.Value &&
                challenge.CompetitionId == signal.CompetitionId &&
                !challenge.IsDeleting,
                ct);
        if (!challengeExists)
            return;

        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == signal.CompetitionId, ct);
        if (competition is null)
            return;

        await writer.WriteAsync(new ScoreEventCreate(
            CompetitionId: signal.CompetitionId,
            TeamId: signal.TeamId,
            ScoringKey: ScoringKey,
            EventType: "awdp.patch-verified",
            PointsDelta: competition.DefensePoints ?? 100,
            IdempotencyKey: $"patch:{signal.IdempotencyKey}",
            ChallengeId: signal.SubjectId,
            SourceSignalId: signal.Id,
            Reason: "Patch verified",
            Timestamp: signal.OccurredAt), ct);
    }
}

public class ControlIntervalScoringStrategy(ApplicationDbContext db, IScoreEventWriter writer) : IBatchScoringStrategy
{
    public string ScoringKey => ScoringKeys.ControlInterval;

    public bool CanHandle(ScoreSignal signal)
        => signal.SignalType == ScoreSignalTypes.ControlHeld;

    public Task HandleAsync(ScoreSignal signal, CancellationToken ct = default)
        => HandleBatchAsync([signal], ct);

    public async Task HandleBatchAsync(
        IReadOnlyCollection<ScoreSignal> signals,
        CancellationToken ct = default)
    {
        var eligibleSignals = signals
            .Where(signal =>
                CanHandle(signal) &&
                signal.SubjectType == "challenge" &&
                signal.SubjectId.HasValue)
            .ToList();
        if (eligibleSignals.Count == 0)
            return;

        var events = new List<ScoreEventCreate>(eligibleSignals.Count);
        foreach (var competitionGroup in eligibleSignals.GroupBy(signal => signal.CompetitionId))
        {
            var challengeIds = competitionGroup
                .Select(signal => signal.SubjectId!.Value)
                .Distinct()
                .ToArray();
            var activeChallengeIds = (await db.Challenges
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(challenge =>
                        challenge.CompetitionId == competitionGroup.Key &&
                        challengeIds.Contains(challenge.Id) &&
                        !challenge.IsDeleting)
                    .Select(challenge => challenge.Id)
                    .ToListAsync(ct))
                .ToHashSet();
            if (activeChallengeIds.Count == 0)
                continue;

            var points = await db.Competitions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(competition => competition.Id == competitionGroup.Key)
                .Select(competition => (int?)competition.ControlPointsPerInterval)
                .FirstOrDefaultAsync(ct) ?? 10;

            events.AddRange(competitionGroup
                .Where(signal => activeChallengeIds.Contains(signal.SubjectId!.Value))
                .Select(signal => new ScoreEventCreate(
                    CompetitionId: signal.CompetitionId,
                    TeamId: signal.TeamId,
                    ScoringKey: ScoringKey,
                    EventType: "koh.control-interval",
                    PointsDelta: points,
                    IdempotencyKey: $"koh:{signal.IdempotencyKey}",
                    ChallengeId: signal.SubjectId,
                    SourceSignalId: signal.Id,
                    Reason: "Control interval held",
                    MetadataJson: signal.PayloadJson,
                    Timestamp: signal.OccurredAt)));
        }

        if (events.Count > 0)
            await writer.WriteBatchAsync(events, ct);
    }
}

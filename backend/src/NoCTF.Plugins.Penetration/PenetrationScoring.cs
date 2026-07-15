using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.Penetration;

public sealed class PenetrationScoringProfileContributor(ApplicationDbContext db) : IScoringProfileContributor
{
    public async Task<IReadOnlyCollection<string>> GetAdditionalScoringKeysAsync(Competition competition, CancellationToken ct = default)
    {
        if (competition.GameModeType != GameModeType.Ctf &&
            !string.Equals(competition.ModeKey, "ctf", StringComparison.OrdinalIgnoreCase))
            return [];

        var hasPenetrationChallenges = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c =>
                c.CompetitionId == competition.Id &&
                c.TypeId.ToLower() == PenetrationConstants.TypeId &&
                !c.IsDeleting,
                ct);

        return hasPenetrationChallenges
            ? [ScoringKeys.PenetrationStage, ScoringKeys.PenetrationBloodBonus]
            : [];
    }
}

public class PenetrationStageScoringStrategy(ApplicationDbContext db, IScoreEventWriter writer) : IScoringStrategy
{
    public string ScoringKey => ScoringKeys.PenetrationStage;

    public bool CanHandle(ScoreSignal signal)
        => signal.SignalType == ScoreSignalTypes.PenetrationFlagAccepted && signal.SubjectId.HasValue;

    public async Task HandleAsync(ScoreSignal signal, CancellationToken ct = default)
    {
        var flag = await db.PenetrationFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.CompetitionId == signal.CompetitionId && f.Id == signal.SubjectId!.Value, ct);
        if (flag is null) return;

        await writer.WriteAsync(new ScoreEventCreate(
            CompetitionId: signal.CompetitionId,
            TeamId: signal.TeamId,
            ScoringKey: ScoringKey,
            EventType: "penetration.stage-solve",
            PointsDelta: flag.Score,
            IdempotencyKey: $"penetration-stage:{signal.TeamId:N}:{flag.ChallengeId:N}:{flag.Id:N}",
            ChallengeId: flag.ChallengeId,
            SourceSignalId: signal.Id,
            Reason: $"Solved penetration stage {flag.Stage}: {flag.Name}",
            MetadataJson: ScoringJson.Serialize(new
            {
                flagId = flag.Id,
                flag.Stage,
                flagName = flag.Name,
                baseScore = flag.Score
            }),
            Timestamp: signal.OccurredAt), ct);
    }
}

public class PenetrationBloodBonusStrategy(ApplicationDbContext db, IScoreEventWriter writer) : IScoringStrategy
{
    public string ScoringKey => ScoringKeys.PenetrationBloodBonus;

    public bool CanHandle(ScoreSignal signal)
        => signal.SignalType == ScoreSignalTypes.PenetrationFlagAccepted && signal.SubjectId.HasValue;

    public async Task HandleAsync(ScoreSignal signal, CancellationToken ct = default)
    {
        var flag = await db.PenetrationFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.CompetitionId == signal.CompetitionId && f.Id == signal.SubjectId!.Value, ct);
        if (flag is null) return;

        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.CompetitionId == signal.CompetitionId &&
                c.Id == flag.ChallengeId &&
                !c.IsDeleting,
                ct);
        if (challenge is null || !challenge.EnableBloodBonus) return;

        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(c => c.Id == signal.CompetitionId, ct);

        var solves = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == signal.CompetitionId &&
                s.ChallengeId == flag.ChallengeId &&
                s.PenetrationFlagId == flag.Id &&
                s.IsCorrect)
            .Join(
                db.Teams.IgnoreQueryFilters().AsNoTracking().Where(t =>
                    t.CompetitionId == signal.CompetitionId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned),
                s => s.TeamId,
                t => t.Id,
                (s, _) => new { s.TeamId, s.SubmittedAt })
            .GroupBy(s => s.TeamId)
            .Select(g => new { TeamId = g.Key, SubmittedAt = g.Min(s => s.SubmittedAt) })
            .OrderBy(s => s.SubmittedAt)
            .ToListAsync(ct);

        foreach (var solve in solves.Select((value, index) => new { value.TeamId, Rank = index + 1 }).Where(s => s.Rank <= 3))
        {
            var bonus = CtfScoreCalculator.CalculateBloodBonus(solve.Rank, flag.Score, competition, challenge.EnableBloodBonus);
            await writer.WriteAsync(new ScoreEventCreate(
                CompetitionId: signal.CompetitionId,
                TeamId: solve.TeamId,
                ScoringKey: ScoringKey,
                EventType: "penetration.stage-blood-bonus",
                PointsDelta: bonus,
                IdempotencyKey: $"penetration-blood:{solve.TeamId:N}:{flag.ChallengeId:N}:{flag.Id:N}:rank:{solve.Rank}:bonus:{bonus}",
                ChallengeId: flag.ChallengeId,
                SourceSignalId: signal.Id,
                Reason: $"Stage {flag.Stage} blood bonus",
                MetadataJson: ScoringJson.Serialize(new
                {
                    flagId = flag.Id,
                    flag.Stage,
                    flagName = flag.Name,
                    baseScore = flag.Score,
                    solveRank = solve.Rank,
                    bonusPoints = bonus
                }),
                Timestamp: signal.OccurredAt), ct);
        }
    }
}

public sealed class PenetrationScoreRebuildContributor(ApplicationDbContext db) : IScoreRebuildContributor
{
    public IReadOnlySet<string> OwnedChallengeTypeIds { get; } =
        new HashSet<string>([PenetrationConstants.TypeId], StringComparer.OrdinalIgnoreCase);

    private sealed record RankedSolve(Guid TeamId, DateTime SubmittedAt, int Rank, Guid? SignalId);

    public async Task RebuildAsync(
        Competition competition,
        IReadOnlyCollection<Guid> challengeIds,
        bool rebuildEntireCompetition,
        CancellationToken ct = default)
    {
        if (competition.GameModeType != GameModeType.Ctf &&
            !string.Equals(competition.ModeKey, "ctf", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c =>
                c.CompetitionId == competition.Id &&
                challengeIds.Contains(c.Id) &&
                c.TypeId.ToLower() == PenetrationConstants.TypeId &&
                !c.IsDeleting)
            .ToListAsync(ct);

        var penetrationChallengeIds = challenges.Select(c => c.Id).ToHashSet();
        var staleQuery = db.ScoreEvents
            .IgnoreQueryFilters()
            .Where(e =>
                e.CompetitionId == competition.Id &&
                (e.ScoringKey == ScoringKeys.PenetrationStage ||
                 e.ScoringKey == ScoringKeys.PenetrationBloodBonus));

        if (!rebuildEntireCompetition)
        {
            staleQuery = staleQuery.Where(e =>
                e.ChallengeId.HasValue && penetrationChallengeIds.Contains(e.ChallengeId.Value));
        }

        if (db.Database.IsRelational())
            await staleQuery.ExecuteDeleteAsync(ct);
        else
            db.ScoreEvents.RemoveRange(await staleQuery.ToListAsync(ct));

        if (challenges.Count == 0)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var challengeMap = challenges.ToDictionary(c => c.Id);
        var flags = await db.PenetrationFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f =>
                f.CompetitionId == competition.Id &&
                penetrationChallengeIds.Contains(f.ChallengeId))
            .OrderBy(f => f.ChallengeId)
            .ThenBy(f => f.Stage)
            .ToListAsync(ct);
        var rankedSolvesByFlag = await GetRankedSolvesAsync(
            competition.Id,
            flags.Select(flag => flag.Id).ToArray(),
            ct);

        var rebuiltEvents = new List<ScoreEvent>();
        foreach (var flag in flags)
        {
            var solves = rankedSolvesByFlag.GetValueOrDefault(flag.Id) ?? [];
            foreach (var solve in solves)
            {
                rebuiltEvents.Add(new ScoreEvent
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competition.Id,
                    TeamId = solve.TeamId,
                    ChallengeId = flag.ChallengeId,
                    ScoringKey = ScoringKeys.PenetrationStage,
                    EventType = "penetration.stage-solve.rebuilt",
                    PointsDelta = flag.Score,
                    Reason = $"Rebuilt penetration stage {flag.Stage}: {flag.Name}",
                    Timestamp = solve.SubmittedAt,
                    SourceSignalId = solve.SignalId,
                    IdempotencyKey = $"penetration-stage:{solve.TeamId:N}:{flag.ChallengeId:N}:{flag.Id:N}",
                    MetadataJson = ScoringJson.Serialize(new
                    {
                        flagId = flag.Id,
                        flag.Stage,
                        flagName = flag.Name,
                        baseScore = flag.Score,
                        rebuilt = true
                    })
                });

                if (solve.Rank > 3 || !challengeMap[flag.ChallengeId].EnableBloodBonus)
                    continue;

                var bonus = CtfScoreCalculator.CalculateBloodBonus(
                    solve.Rank,
                    flag.Score,
                    competition,
                    challengeMap[flag.ChallengeId].EnableBloodBonus);
                if (bonus == 0)
                    continue;

                rebuiltEvents.Add(new ScoreEvent
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competition.Id,
                    TeamId = solve.TeamId,
                    ChallengeId = flag.ChallengeId,
                    ScoringKey = ScoringKeys.PenetrationBloodBonus,
                    EventType = "penetration.stage-blood-bonus.rebuilt",
                    PointsDelta = bonus,
                    Reason = $"Rebuilt stage {flag.Stage} blood bonus",
                    Timestamp = solve.SubmittedAt,
                    SourceSignalId = solve.SignalId,
                    IdempotencyKey = $"penetration-blood:{solve.TeamId:N}:{flag.ChallengeId:N}:{flag.Id:N}:rank:{solve.Rank}:bonus:{bonus}",
                    MetadataJson = ScoringJson.Serialize(new
                    {
                        flagId = flag.Id,
                        flag.Stage,
                        flagName = flag.Name,
                        baseScore = flag.Score,
                        solveRank = solve.Rank,
                        bonusPoints = bonus,
                        rebuilt = true
                    })
                });
            }
        }

        db.ScoreEvents.AddRange(rebuiltEvents);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<Guid, List<RankedSolve>>> GetRankedSolvesAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid> flagIds,
        CancellationToken ct)
    {
        if (flagIds.Count == 0)
            return [];

        var submissions = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                s.PenetrationFlagId.HasValue &&
                flagIds.Contains(s.PenetrationFlagId.Value) &&
                s.IsCorrect)
            .Join(
                db.Teams.IgnoreQueryFilters().AsNoTracking().Where(t =>
                    t.CompetitionId == competitionId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned),
                s => s.TeamId,
                t => t.Id,
                (s, _) => new { FlagId = s.PenetrationFlagId!.Value, s.TeamId, s.SubmittedAt })
            .GroupBy(s => new { s.FlagId, s.TeamId })
            .Select(g => new
            {
                g.Key.FlagId,
                g.Key.TeamId,
                SubmittedAt = g.Min(s => s.SubmittedAt)
            })
            .ToListAsync(ct);

        var teamIds = submissions.Select(s => s.TeamId).ToHashSet();
        var signalRows = await db.ScoreSignals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                s.SubjectId.HasValue &&
                flagIds.Contains(s.SubjectId.Value) &&
                s.SignalType == ScoreSignalTypes.PenetrationFlagAccepted &&
                teamIds.Contains(s.TeamId))
            .GroupBy(s => new { FlagId = s.SubjectId!.Value, s.TeamId })
            .Select(g => new
            {
                g.Key.FlagId,
                g.Key.TeamId,
                SignalId = g.OrderBy(s => s.OccurredAt).Select(s => s.Id).FirstOrDefault()
            })
            .ToListAsync(ct);
        var signalMap = signalRows.ToDictionary(
            signal => (signal.FlagId, signal.TeamId),
            signal => (Guid?)signal.SignalId);

        return submissions
            .GroupBy(solve => solve.FlagId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(solve => solve.SubmittedAt)
                    .Select((solve, index) => new RankedSolve(
                        solve.TeamId,
                        solve.SubmittedAt,
                        index + 1,
                        signalMap.GetValueOrDefault((solve.FlagId, solve.TeamId))))
                    .ToList());
    }
}

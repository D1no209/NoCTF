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
                c.TypeId.ToLower() == PenetrationConstants.TypeId,
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
            .FirstOrDefaultAsync(c => c.CompetitionId == signal.CompetitionId && c.Id == flag.ChallengeId, ct);
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

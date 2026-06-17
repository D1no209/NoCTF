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
            .FirstAsync(c => c.Id == challengeId && c.CompetitionId == signal.CompetitionId, ct);

        var solves = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == signal.CompetitionId &&
                s.ChallengeId == challengeId &&
                s.IsCorrect)
            .OrderBy(s => s.SubmittedAt)
            .Select(s => new { s.TeamId, s.SubmittedAt })
            .ToListAsync(ct);

        if (solves.Count == 0)
            return;

        var currentPoints = CalculateDynamicPoints(solves.Count, challenge.PointsConfig);
        foreach (var solve in solves)
        {
            var currentTotal = await db.ScoreEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(e =>
                    e.CompetitionId == signal.CompetitionId &&
                    e.TeamId == solve.TeamId &&
                    e.ChallengeId == challengeId &&
                    e.ScoringKey == ScoringKey)
                .SumAsync(e => (long)e.PointsDelta, ct);

            var delta = currentPoints - (int)currentTotal;
            var eventType = currentTotal == 0 ? "ctf.solve" : "ctf.score-adjustment";
            var suffix = currentTotal == 0 ? "solve" : $"correction:{solves.Count}";

            await writer.WriteAsync(new ScoreEventCreate(
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
                MetadataJson: ScoringJson.Serialize(new { solveCount = solves.Count, currentPoints }),
                Timestamp: signal.OccurredAt), ct);
        }
    }

    private static int CalculateDynamicPoints(int solveCount, PointsConfig config)
    {
        if (solveCount <= 0)
            return config.InitialPoints;

        var initial = (double)config.InitialPoints;
        var min = (double)config.MinimumPoints;
        var decay = (double)config.DecayFactor;
        var solves = (double)solveCount;
        var value = ((min - initial) / (decay * decay)) * (solves * solves) + initial;
        return (int)Math.Round(Math.Max(min, Math.Min(initial, value)));
    }
}

public class RoundAccumulationScoringStrategy(ApplicationDbContext db, IScoreEventWriter writer) : IScoringStrategy
{
    public string ScoringKey => ScoringKeys.RoundAccumulation;

    public bool CanHandle(ScoreSignal signal)
        => signal.SignalType is ScoreSignalTypes.AttackAccepted
            or ScoreSignalTypes.ServiceCheckPassed
            or ScoreSignalTypes.ServiceCheckFailed
            or ScoreSignalTypes.ServiceAttacked;

    public async Task HandleAsync(ScoreSignal signal, CancellationToken ct = default)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(c => c.Id == signal.CompetitionId, ct);

        var points = signal.SignalType switch
        {
            ScoreSignalTypes.AttackAccepted => competition.AttackPoints ?? 50,
            ScoreSignalTypes.ServiceCheckPassed => competition.ServiceOnlinePoints ?? 100,
            ScoreSignalTypes.ServiceCheckFailed => -(competition.ServiceDownPenalty ?? 50),
            ScoreSignalTypes.ServiceAttacked => -(competition.BeenAttackedPenalty ?? 50),
            _ => 0
        };

        var challengeId = signal.SubjectType == "challenge" ? signal.SubjectId : null;
        await writer.WriteAsync(new ScoreEventCreate(
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
            Timestamp: signal.OccurredAt), ct);
    }
}

public class OneShotVerificationScoringStrategy(ApplicationDbContext db, IScoreEventWriter writer) : IScoringStrategy
{
    public string ScoringKey => ScoringKeys.OneShotVerification;

    public bool CanHandle(ScoreSignal signal)
        => signal.SignalType == ScoreSignalTypes.PatchVerified;

    public async Task HandleAsync(ScoreSignal signal, CancellationToken ct = default)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(c => c.Id == signal.CompetitionId, ct);

        await writer.WriteAsync(new ScoreEventCreate(
            CompetitionId: signal.CompetitionId,
            TeamId: signal.TeamId,
            ScoringKey: ScoringKey,
            EventType: "awdp.patch-verified",
            PointsDelta: competition.DefensePoints ?? 100,
            IdempotencyKey: $"patch:{signal.IdempotencyKey}",
            ChallengeId: signal.SubjectType == "challenge" ? signal.SubjectId : null,
            SourceSignalId: signal.Id,
            Reason: "Patch verified",
            Timestamp: signal.OccurredAt), ct);
    }
}

public class ControlIntervalScoringStrategy(ApplicationDbContext db, IScoreEventWriter writer) : IScoringStrategy
{
    public string ScoringKey => ScoringKeys.ControlInterval;

    public bool CanHandle(ScoreSignal signal)
        => signal.SignalType == ScoreSignalTypes.ControlHeld;

    public async Task HandleAsync(ScoreSignal signal, CancellationToken ct = default)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(c => c.Id == signal.CompetitionId, ct);

        await writer.WriteAsync(new ScoreEventCreate(
            CompetitionId: signal.CompetitionId,
            TeamId: signal.TeamId,
            ScoringKey: ScoringKey,
            EventType: "koh.control-interval",
            PointsDelta: competition.ControlPointsPerInterval ?? 10,
            IdempotencyKey: $"koh:{signal.IdempotencyKey}",
            ChallengeId: signal.SubjectType == "challenge" ? signal.SubjectId : null,
            SourceSignalId: signal.Id,
            Reason: "Control interval held",
            MetadataJson: signal.PayloadJson,
            Timestamp: signal.OccurredAt), ct);
    }
}

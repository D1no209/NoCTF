using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Application.Scoring;

public class CompetitionScoringProfileResolver(
    ApplicationDbContext db,
    IEnumerable<IScoringProfileContributor>? contributors = null) : ICompetitionScoringProfileResolver
{
    public async Task<IReadOnlySet<string>> ResolveAsync(Guid competitionId, CancellationToken ct = default)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(c => c.Id == competitionId, ct);

        HashSet<string> profile;

        if (!string.IsNullOrWhiteSpace(competition.ScoringProfileJson))
        {
            var configured = ScoringJson.Deserialize<string[]>(competition.ScoringProfileJson);
            if (configured is { Length: > 0 })
            {
                if (competition.GameModeType == GameModeType.Awdp || string.Equals(competition.ModeKey, "awdp", StringComparison.OrdinalIgnoreCase))
                {
                    profile = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ScoringKeys.AwdpRound };
                    return await AddContributorKeysAsync(profile, competition, ct);
                }

                profile = configured.ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (competition.GameModeType == GameModeType.Ctf || string.Equals(competition.ModeKey, "ctf", StringComparison.OrdinalIgnoreCase))
                    profile.Add(ScoringKeys.BloodBonus);
                return await AddContributorKeysAsync(profile, competition, ct);
            }
        }

        var modeKey = string.IsNullOrWhiteSpace(competition.ModeKey)
            ? competition.GameModeType.ToString().ToLowerInvariant()
            : competition.ModeKey.Trim().ToLowerInvariant();

        profile = modeKey switch
        {
            "ctf" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ScoringKeys.DecaySolve, ScoringKeys.BloodBonus },
            "awd" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ScoringKeys.RoundAccumulation },
            "awdp" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ScoringKeys.AwdpRound
            },
            "koh" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ScoringKeys.ControlInterval },
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

        return await AddContributorKeysAsync(profile, competition, ct);
    }

    private async Task<IReadOnlySet<string>> AddContributorKeysAsync(
        HashSet<string> profile,
        Competition competition,
        CancellationToken ct)
    {
        foreach (var contributor in contributors ?? [])
        {
            foreach (var key in await contributor.GetAdditionalScoringKeysAsync(competition, ct))
                profile.Add(key);
        }

        return profile;
    }
}

public class ScoreSignalEmitter(
    ApplicationDbContext db,
    IEnumerable<IScoringStrategy> strategies,
    ICompetitionScoringProfileResolver profileResolver,
    ILogger<ScoreSignalEmitter> logger) : IScoreSignalEmitter
{
    public async Task<ScoreSignal> EmitAsync(ScoreSignalCreate signal, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(signal.IdempotencyKey))
            throw new ArgumentException("Score signal idempotency key is required.", nameof(signal));

        var existing = await db.ScoreSignals
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s =>
                s.CompetitionId == signal.CompetitionId &&
                s.IdempotencyKey == signal.IdempotencyKey, ct);

        var entity = existing ?? new ScoreSignal
        {
            Id = Guid.NewGuid(),
            CompetitionId = signal.CompetitionId,
            TeamId = signal.TeamId,
            ActorUserId = signal.ActorUserId,
            SubjectType = signal.SubjectType,
            SubjectId = signal.SubjectId,
            SignalType = signal.SignalType,
            OccurredAt = signal.OccurredAt ?? DateTime.UtcNow,
            RoundNumber = signal.RoundNumber,
            PayloadJson = string.IsNullOrWhiteSpace(signal.PayloadJson) ? "{}" : signal.PayloadJson,
            IdempotencyKey = signal.IdempotencyKey
        };

        if (existing is null)
        {
            db.ScoreSignals.Add(entity);
            await db.SaveChangesAsync(ct);
        }

        var profile = await profileResolver.ResolveAsync(signal.CompetitionId, ct);
        foreach (var strategy in strategies.Where(s =>
                     profile.Contains(s.ScoringKey) && s.CanHandle(entity)))
        {
            await strategy.HandleAsync(entity, ct);
        }

        if (existing is not null)
        {
            logger.LogDebug(
                "Reprocessed existing score signal {SignalId} with idempotency key {IdempotencyKey}.",
                entity.Id,
                entity.IdempotencyKey);
        }

        return entity;
    }
}

public class ScoreEventWriter(ApplicationDbContext db) : IScoreEventWriter
{
    public async Task<ScoreEvent?> WriteAsync(ScoreEventCreate scoreEvent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scoreEvent.IdempotencyKey))
            throw new ArgumentException("Score event idempotency key is required.", nameof(scoreEvent));

        var existing = await db.ScoreEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e =>
                e.CompetitionId == scoreEvent.CompetitionId &&
                e.IdempotencyKey == scoreEvent.IdempotencyKey, ct);

        if (existing is not null)
            return existing;

        if (scoreEvent.PointsDelta == 0)
            return null;

        var entity = new ScoreEvent
        {
            Id = Guid.NewGuid(),
            CompetitionId = scoreEvent.CompetitionId,
            TeamId = scoreEvent.TeamId,
            ChallengeId = scoreEvent.ChallengeId,
            ScoringKey = scoreEvent.ScoringKey,
            EventType = scoreEvent.EventType,
            PointsDelta = scoreEvent.PointsDelta,
            Reason = scoreEvent.Reason,
            Timestamp = scoreEvent.Timestamp ?? DateTime.UtcNow,
            RoundNumber = scoreEvent.RoundNumber,
            SourceSignalId = scoreEvent.SourceSignalId,
            IdempotencyKey = scoreEvent.IdempotencyKey,
            MetadataJson = string.IsNullOrWhiteSpace(scoreEvent.MetadataJson) ? "{}" : scoreEvent.MetadataJson
        };

        db.ScoreEvents.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }
}

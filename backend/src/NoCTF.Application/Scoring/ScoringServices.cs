using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Application.CompetitionModes;

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
    private readonly record struct SignalKey(Guid CompetitionId, string IdempotencyKey);
    private readonly IReadOnlyCollection<IScoringStrategy> _strategies =
        ProviderRegistry.BuildUnique(strategies, strategy => strategy.ScoringKey, "scoring strategy")
            .Values
            .ToArray();

    public async Task<ScoreSignal> EmitAsync(ScoreSignalCreate signal, CancellationToken ct = default)
        => (await EmitBatchAsync([signal], ct))[0];

    public async Task<ScoreSignal> PersistAsync(ScoreSignalCreate signal, CancellationToken ct = default)
        => (await PersistBatchAsync([signal], ct))[0];

    public Task<IReadOnlyList<ScoreSignal>> EmitBatchAsync(
        IReadOnlyCollection<ScoreSignalCreate> signals,
        CancellationToken ct = default)
        => PersistAndOptionallyScoreAsync(signals, applyStrategies: true, ct);

    public Task<IReadOnlyList<ScoreSignal>> PersistBatchAsync(
        IReadOnlyCollection<ScoreSignalCreate> signals,
        CancellationToken ct = default)
        => PersistAndOptionallyScoreAsync(signals, applyStrategies: false, ct);

    private async Task<IReadOnlyList<ScoreSignal>> PersistAndOptionallyScoreAsync(
        IReadOnlyCollection<ScoreSignalCreate> signals,
        bool applyStrategies,
        CancellationToken ct)
    {
        if (signals.Count == 0)
            return [];

        var input = signals.ToList();
        if (input.Any(signal => string.IsNullOrWhiteSpace(signal.IdempotencyKey)))
            throw new ArgumentException("Score signal idempotency key is required.", nameof(signals));

        var uniqueCreates = input
            .GroupBy(signal => new SignalKey(signal.CompetitionId, signal.IdempotencyKey))
            .Select(group => group.First())
            .ToList();
        var entities = await LoadSignalsAsync(uniqueCreates, ct);
        var existingKeys = entities.Keys.ToHashSet();
        var missing = uniqueCreates
            .Where(signal => !entities.ContainsKey(new SignalKey(signal.CompetitionId, signal.IdempotencyKey)))
            .ToList();

        DbUpdateException? lastConflict = null;
        for (var attempt = 0; missing.Count > 0 && attempt < 3; attempt++)
        {
            var pending = missing.Select(CreateEntity).ToList();
            db.ScoreSignals.AddRange(pending);
            try
            {
                await db.SaveChangesAsync(ct);
                foreach (var entity in pending)
                    entities[new SignalKey(entity.CompetitionId, entity.IdempotencyKey)] = entity;
                missing.Clear();
            }
            catch (DbUpdateException ex)
            {
                lastConflict = ex;
                foreach (var entity in pending)
                    db.Entry(entity).State = EntityState.Detached;

                var concurrent = await LoadSignalsAsync(missing, ct);
                foreach (var pair in concurrent)
                    entities[pair.Key] = pair.Value;
                missing = missing
                    .Where(signal => !entities.ContainsKey(new SignalKey(signal.CompetitionId, signal.IdempotencyKey)))
                    .ToList();
            }
        }

        if (missing.Count > 0)
            throw lastConflict ?? new DbUpdateException("Failed to persist score signal batch.");

        var uniqueEntities = uniqueCreates
            .Select(signal => entities[new SignalKey(signal.CompetitionId, signal.IdempotencyKey)])
            .ToList();
        if (applyStrategies)
        {
            foreach (var competitionGroup in uniqueEntities.GroupBy(signal => signal.CompetitionId))
            {
                var profile = await profileResolver.ResolveAsync(competitionGroup.Key, ct);
                var competitionSignals = competitionGroup.ToList();
                foreach (var strategy in _strategies.Where(strategy => profile.Contains(strategy.ScoringKey)))
                {
                    var matchingSignals = competitionSignals.Where(strategy.CanHandle).ToList();
                    if (matchingSignals.Count == 0)
                        continue;

                    if (strategy is IBatchScoringStrategy batchStrategy)
                        await batchStrategy.HandleBatchAsync(matchingSignals, ct);
                    else
                        foreach (var entity in matchingSignals)
                            await strategy.HandleAsync(entity, ct);
                }
            }
        }

        foreach (var entity in uniqueEntities.Where(entity =>
                     existingKeys.Contains(new SignalKey(entity.CompetitionId, entity.IdempotencyKey))))
        {
            logger.LogDebug(
                "Reprocessed existing score signal {SignalId} with idempotency key {IdempotencyKey}.",
                entity.Id,
                entity.IdempotencyKey);
        }

        return input
            .Select(signal => entities[new SignalKey(signal.CompetitionId, signal.IdempotencyKey)])
            .ToList();
    }

    private async Task<Dictionary<SignalKey, ScoreSignal>> LoadSignalsAsync(
        IReadOnlyCollection<ScoreSignalCreate> signals,
        CancellationToken ct)
    {
        var result = new Dictionary<SignalKey, ScoreSignal>();
        foreach (var group in signals.GroupBy(signal => signal.CompetitionId))
        {
            var keys = group.Select(signal => signal.IdempotencyKey).Distinct().ToList();
            var existing = await db.ScoreSignals
                .IgnoreQueryFilters()
                .Where(signal => signal.CompetitionId == group.Key && keys.Contains(signal.IdempotencyKey))
                .ToListAsync(ct);
            foreach (var entity in existing)
                result[new SignalKey(entity.CompetitionId, entity.IdempotencyKey)] = entity;
        }
        return result;
    }

    private static ScoreSignal CreateEntity(ScoreSignalCreate signal)
        => new()
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
}

public class ScoreEventWriter(ApplicationDbContext db) : IScoreEventWriter
{
    private readonly record struct EventKey(Guid CompetitionId, string IdempotencyKey);

    public async Task<ScoreEvent?> WriteAsync(ScoreEventCreate scoreEvent, CancellationToken ct = default)
        => (await WriteBatchAsync([scoreEvent], ct))[0];

    public async Task<IReadOnlyList<ScoreEvent?>> WriteBatchAsync(
        IReadOnlyCollection<ScoreEventCreate> scoreEvents,
        CancellationToken ct = default)
    {
        if (scoreEvents.Count == 0)
            return [];

        var input = scoreEvents.ToList();
        if (input.Any(scoreEvent => string.IsNullOrWhiteSpace(scoreEvent.IdempotencyKey)))
            throw new ArgumentException("Score event idempotency key is required.", nameof(scoreEvents));

        var uniqueCreates = input
            .GroupBy(scoreEvent => new EventKey(scoreEvent.CompetitionId, scoreEvent.IdempotencyKey))
            .Select(group => group.First())
            .ToList();
        var entities = await LoadEventsAsync(uniqueCreates, ct);
        var missing = uniqueCreates
            .Where(scoreEvent =>
                scoreEvent.PointsDelta != 0 &&
                !entities.ContainsKey(new EventKey(scoreEvent.CompetitionId, scoreEvent.IdempotencyKey)))
            .ToList();

        DbUpdateException? lastConflict = null;
        for (var attempt = 0; missing.Count > 0 && attempt < 3; attempt++)
        {
            var pending = missing.Select(CreateEntity).ToList();
            db.ScoreEvents.AddRange(pending);
            try
            {
                await db.SaveChangesAsync(ct);
                foreach (var entity in pending)
                    entities[new EventKey(entity.CompetitionId, entity.IdempotencyKey)] = entity;
                missing.Clear();
            }
            catch (DbUpdateException ex)
            {
                lastConflict = ex;
                foreach (var entity in pending)
                    db.Entry(entity).State = EntityState.Detached;

                var concurrent = await LoadEventsAsync(missing, ct);
                foreach (var pair in concurrent)
                    entities[pair.Key] = pair.Value;
                missing = missing
                    .Where(scoreEvent => !entities.ContainsKey(new EventKey(scoreEvent.CompetitionId, scoreEvent.IdempotencyKey)))
                    .ToList();
            }
        }

        if (missing.Count > 0)
            throw lastConflict ?? new DbUpdateException("Failed to persist score event batch.");

        return input
            .Select(scoreEvent => entities.GetValueOrDefault(
                new EventKey(scoreEvent.CompetitionId, scoreEvent.IdempotencyKey)))
            .ToList();
    }

    private async Task<Dictionary<EventKey, ScoreEvent>> LoadEventsAsync(
        IReadOnlyCollection<ScoreEventCreate> scoreEvents,
        CancellationToken ct)
    {
        var result = new Dictionary<EventKey, ScoreEvent>();
        foreach (var group in scoreEvents.GroupBy(scoreEvent => scoreEvent.CompetitionId))
        {
            var keys = group.Select(scoreEvent => scoreEvent.IdempotencyKey).Distinct().ToList();
            var existing = await db.ScoreEvents
                .IgnoreQueryFilters()
                .Where(scoreEvent => scoreEvent.CompetitionId == group.Key && keys.Contains(scoreEvent.IdempotencyKey))
                .ToListAsync(ct);
            foreach (var entity in existing)
                result[new EventKey(entity.CompetitionId, entity.IdempotencyKey)] = entity;
        }
        return result;
    }

    internal static ScoreEvent CreateEntity(ScoreEventCreate scoreEvent)
        => new()
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
}

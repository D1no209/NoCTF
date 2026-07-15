using System.Text.Json;
using NoCTF.Core;

namespace NoCTF.Application.Scoring;

public static class ScoreSignalTypes
{
    public const string SolveAccepted = "solve.accepted";
    public const string PenetrationFlagAccepted = "penetration.flag.accepted";
    public const string AttackAccepted = "attack.accepted";
    public const string ServiceCheckPassed = "service.check.passed";
    public const string ServiceCheckFailed = "service.check.failed";
    public const string ServiceAttacked = "service.attacked";
    public const string PatchVerified = "patch.verified";
    public const string ControlHeld = "control.held";
}

public static class ScoringKeys
{
    public const string DecaySolve = "decay-solve";
    public const string BloodBonus = "blood-bonus";
    public const string PenetrationStage = "penetration-stage";
    public const string PenetrationBloodBonus = "penetration-blood-bonus";
    public const string RoundAccumulation = "round-accumulation";
    public const string AwdpRound = "awdp-round";
    public const string OneShotVerification = "one-shot-verification";
    public const string ControlInterval = "control-interval";
}

public sealed record ScoreSignalCreate(
    Guid CompetitionId,
    Guid TeamId,
    string SignalType,
    string IdempotencyKey,
    string SubjectType = "",
    Guid? SubjectId = null,
    Guid? ActorUserId = null,
    int? RoundNumber = null,
    string PayloadJson = "{}",
    DateTime? OccurredAt = null);

public sealed record ScoreEventCreate(
    Guid CompetitionId,
    Guid TeamId,
    string ScoringKey,
    string EventType,
    int PointsDelta,
    string IdempotencyKey,
    Guid? ChallengeId = null,
    Guid? SourceSignalId = null,
    string? Reason = null,
    int? RoundNumber = null,
    string MetadataJson = "{}",
    DateTime? Timestamp = null);

public sealed record ScoreboardRow(
    int Rank,
    Guid TeamId,
    string TeamName,
    long TotalScore,
    IReadOnlyDictionary<string, long> Metrics,
    IReadOnlyDictionary<string, string> TieBreakers);

public interface IScoreSignalEmitter
{
    Task<ScoreSignal> EmitAsync(ScoreSignalCreate signal, CancellationToken ct = default);

    // Persist-only is used by workflows that rebuild the complete score
    // projection immediately afterwards. The default preserves compatibility
    // with external emitters that only implement EmitAsync.
    Task<ScoreSignal> PersistAsync(ScoreSignalCreate signal, CancellationToken ct = default)
        => EmitAsync(signal, ct);

    async Task<IReadOnlyList<ScoreSignal>> EmitBatchAsync(
        IReadOnlyCollection<ScoreSignalCreate> signals,
        CancellationToken ct = default)
    {
        var emitted = new List<ScoreSignal>(signals.Count);
        foreach (var signal in signals)
            emitted.Add(await EmitAsync(signal, ct));
        return emitted;
    }

    async Task<IReadOnlyList<ScoreSignal>> PersistBatchAsync(
        IReadOnlyCollection<ScoreSignalCreate> signals,
        CancellationToken ct = default)
    {
        var persisted = new List<ScoreSignal>(signals.Count);
        foreach (var signal in signals)
            persisted.Add(await PersistAsync(signal, ct));
        return persisted;
    }
}

public interface IScoreEventWriter
{
    Task<ScoreEvent?> WriteAsync(ScoreEventCreate scoreEvent, CancellationToken ct = default);

    async Task<IReadOnlyList<ScoreEvent?>> WriteBatchAsync(
        IReadOnlyCollection<ScoreEventCreate> scoreEvents,
        CancellationToken ct = default)
    {
        var written = new List<ScoreEvent?>(scoreEvents.Count);
        foreach (var scoreEvent in scoreEvents)
            written.Add(await WriteAsync(scoreEvent, ct));
        return written;
    }
}

public interface ICtfScoreRebuilder
{
    Task RebuildCompetitionAsync(Guid competitionId, CancellationToken ct = default);
    Task RebuildChallengeAsync(Guid competitionId, Guid challengeId, CancellationToken ct = default);
}

/// <summary>
/// Allows a challenge plugin to rebuild the score events it owns while the
/// competition-wide rebuild lock is held by the application layer.
/// </summary>
public interface IScoreRebuildContributor
{
    IReadOnlySet<string> OwnedChallengeTypeIds
        => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    Task RebuildAsync(
        Competition competition,
        IReadOnlyCollection<Guid> challengeIds,
        bool rebuildEntireCompetition,
        CancellationToken ct = default);
}

public interface IScoringStrategy
{
    string ScoringKey { get; }
    bool CanHandle(ScoreSignal signal);
    Task HandleAsync(ScoreSignal signal, CancellationToken ct = default);
}

/// <summary>
/// Optional additive capability for strategies that can process a set of facts
/// without issuing database work per signal. Strategies that only implement
/// <see cref="IScoringStrategy"/> continue to be invoked one signal at a time.
/// </summary>
public interface IBatchScoringStrategy : IScoringStrategy
{
    Task HandleBatchAsync(IReadOnlyCollection<ScoreSignal> signals, CancellationToken ct = default);
}

public interface ICompetitionScoringProfileResolver
{
    Task<IReadOnlySet<string>> ResolveAsync(Guid competitionId, CancellationToken ct = default);
}

public interface IScoringProfileContributor
{
    Task<IReadOnlyCollection<string>> GetAdditionalScoringKeysAsync(Competition competition, CancellationToken ct = default);
}

public static class ScoringJson
{
    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

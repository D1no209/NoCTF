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
}

public interface IScoreEventWriter
{
    Task<ScoreEvent?> WriteAsync(ScoreEventCreate scoreEvent, CancellationToken ct = default);
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

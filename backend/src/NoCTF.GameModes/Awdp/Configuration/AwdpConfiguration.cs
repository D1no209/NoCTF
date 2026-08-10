using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Domain.Gameplay;

namespace NoCTF.GameModes.Awdp.Configuration;

public enum AchievementSettlement
{
    Milestone,
    PerRound
}

public sealed record AwdpAchievementConfiguration(AchievementSettlement Settlement, long Points);

public sealed record AwdpConfiguration(
    int SchemaVersion,
    int RoundDurationSeconds,
    AwdpAchievementConfiguration Break,
    AwdpAchievementConfiguration Fix,
    long ViolationPenalty = 100,
    long ServiceDownPenalty = 50,
    bool RequireBreakBeforeFix = true,
    long BreakWrongPenalty = 0,
    long FixFailurePenalty = 0,
    int MaxBreakSubmissions = 10,
    int MaxFixSubmissions = 10,
    EvaluationDispatchMode EvaluationDispatchMode = EvaluationDispatchMode.Automatic)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record AwdpChallengeConfiguration(
    int SchemaVersion,
    AwdpAchievementConfiguration? Break,
    AwdpAchievementConfiguration? Fix,
    bool? RequireBreakBeforeFix,
    int? MaxBreakSubmissions,
    int? MaxFixSubmissions,
    ChallengeRuntimeTemplate? Runtime = null,
    string? PatchEntrypoint = null,
    IReadOnlyList<string>? PatchCommand = null,
    int? PatchTimeoutSeconds = null,
    RunnerJobConfiguration? Checker = null,
    int? ReadyTimeoutSeconds = null,
    long? BreakWrongPenalty = null,
    long? FixFailurePenalty = null,
    long? ViolationPenalty = null,
    long? ServiceDownPenalty = null,
    EvaluationDispatchMode? EvaluationDispatchMode = null)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record AwdpEffectiveConfiguration(
    int RoundDurationSeconds,
    AwdpAchievementConfiguration Break,
    AwdpAchievementConfiguration Fix,
    long BreakWrongPenalty,
    long FixFailurePenalty,
    long ViolationPenalty,
    long ServiceDownPenalty,
    bool RequireBreakBeforeFix,
    int MaxBreakSubmissions,
    int MaxFixSubmissions,
    EvaluationDispatchMode EvaluationDispatchMode,
    ChallengeRuntimeTemplate? Runtime,
    string PatchEntrypoint,
    IReadOnlyList<string>? PatchCommand,
    int PatchTimeoutSeconds,
    RunnerJobConfiguration? Checker,
    int ReadyTimeoutSeconds);

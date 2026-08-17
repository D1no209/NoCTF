using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Scoring;

namespace NoCTF.GameModes.Awdp.Configuration;

public enum AwdpFlagInjectionKind
{
    EnvironmentVariable,
    File
}

public sealed record AwdpFlagInjectionConfiguration(
    AwdpFlagInjectionKind Kind,
    string? EnvironmentVariableName = null,
    string? FilePath = null);

public sealed record AwdpConfiguration(
    int SchemaVersion,
    int RoundDurationSeconds,
    ScoreCurveConfiguration Break,
    ScoreCurveConfiguration Fix,
    long ViolationPenalty = 100,
    long ServiceDownPenalty = 50,
    bool RequireBreakBeforeFix = true,
    long BreakWrongPenalty = 0,
    long FixFailurePenalty = 0,
    int MaxBreakSubmissions = 10,
    int MaxFixSubmissions = 10,
    EvaluationDispatchMode EvaluationDispatchMode = EvaluationDispatchMode.Automatic,
    PerTeamFlagTemplate? FlagTemplate = null)
{
    public const int CurrentSchemaVersion = 3;
}

public sealed record AwdpChallengeConfiguration(
    int SchemaVersion,
    ScoreCurveConfiguration? Break,
    ScoreCurveConfiguration? Fix,
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
    EvaluationDispatchMode? EvaluationDispatchMode = null,
    long? MaximumPatchUploadBytes = null,
    PerTeamFlagTemplate? FlagTemplate = null,
    AwdpFlagInjectionConfiguration? FlagInjection = null)
{
    public const int CurrentSchemaVersion = 3;
}

public sealed record AwdpEffectiveConfiguration(
    int RoundDurationSeconds,
    ScoreCurveConfiguration Break,
    ScoreCurveConfiguration Fix,
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
    int ReadyTimeoutSeconds,
    long MaximumPatchUploadBytes,
    PerTeamFlagTemplate FlagTemplate,
    AwdpFlagInjectionConfiguration? FlagInjection);

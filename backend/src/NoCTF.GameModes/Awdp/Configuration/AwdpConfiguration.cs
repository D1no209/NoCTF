using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Scoring;

namespace NoCTF.GameModes.Awdp.Configuration;

public sealed record AwdpConfiguration(
    int SchemaVersion,
    int RoundDurationSeconds,
    ScoreCurveConfiguration Break,
    ScoreCurveConfiguration Fix,
    long FlagWrongPenalty = 0,
    long ExploitSucceededPenalty = 0,
    long ServiceAbnormalPenalty = 0,
    bool RequireBreakBeforeFix = true,
    int MaxBreakSubmissions = 10,
    int MaxFixSubmissions = 10,
    EvaluationDispatchMode EvaluationDispatchMode = EvaluationDispatchMode.Automatic,
    PerTeamFlagTemplate? FlagTemplate = null)
{
    public const int CurrentSchemaVersion = 4;
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
    long? FlagWrongPenalty = null,
    long? ExploitSucceededPenalty = null,
    long? ServiceAbnormalPenalty = null,
    EvaluationDispatchMode? EvaluationDispatchMode = null,
    long? MaximumPatchUploadBytes = null,
    PerTeamFlagTemplate? FlagTemplate = null)
{
    public const int CurrentSchemaVersion = 4;
}

public sealed record AwdpEffectiveConfiguration(
    int RoundDurationSeconds,
    ScoreCurveConfiguration Break,
    ScoreCurveConfiguration Fix,
    long FlagWrongPenalty,
    long ExploitSucceededPenalty,
    long ServiceAbnormalPenalty,
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
    PerTeamFlagTemplate FlagTemplate);

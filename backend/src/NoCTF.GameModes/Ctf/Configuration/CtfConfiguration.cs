using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Domain.Challenges;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Scoring;

namespace NoCTF.GameModes.Ctf.Configuration;

public enum BloodRewardPolicy
{
    FixedPoints,
    InitialPointsPercentage,
    SolveTimePointsPercentage,
    CurrentPointsPercentage
}

public sealed record BloodReward(BloodRewardPolicy Policy, decimal Value);

public sealed record CtfConfiguration(
    int SchemaVersion,
    ScoreCurveConfiguration DefaultScoreCurve,
    IReadOnlyList<BloodReward> BloodRewards,
    long WrongSubmissionPenalty = 0,
    PerTeamFlagTemplate? FlagTemplate = null)
{
    public const int CurrentSchemaVersion = 2;
}

public sealed record CtfChallengeConfiguration(
    int SchemaVersion,
    ScoreCurveConfiguration? ScoreCurve,
    IReadOnlyList<BloodReward>? BloodRewards,
    int? MaxFlagAttempts = null,
    ChallengeRuntimeTemplate? Runtime = null,
    long? WrongSubmissionPenalty = null,
    PerTeamFlagTemplate? FlagTemplate = null,
    CtfInteractionKind InteractionKind = CtfInteractionKind.FlagSubmission,
    int? MaxPatchAttempts = null,
    string? PatchEntrypoint = null,
    IReadOnlyList<string>? PatchCommand = null,
    int? PatchTimeoutSeconds = null,
    RunnerJobConfiguration? Checker = null,
    int? ReadyTimeoutSeconds = null,
    long? MaximumPatchUploadBytes = null,
    bool CheckerFixInput = false,
    bool CheckerAllowRoot = false)
{
    public const int CurrentSchemaVersion = 3;
}

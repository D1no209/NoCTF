using NoCTF.Application.Runtime.Ports;

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
    long ViolationPenalty = 0,
    long ServiceDownPenalty = 0,
    bool RequireBreakBeforeFix = true)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record AwdpChallengeConfiguration(
    int SchemaVersion,
    AwdpAchievementConfiguration? Break,
    AwdpAchievementConfiguration? Fix,
    bool? RequireBreakBeforeFix,
    int MaxBreakAttempts,
    int MaxFixAttempts,
    ChallengeRuntimeTemplate? Runtime = null,
    string PatchEntrypoint = "fix.sh",
    IReadOnlyList<string>? PatchCommand = null,
    int PatchTimeoutSeconds = 60,
    RunnerJobConfiguration? Checker = null,
    int TargetPort = 0,
    int ReadyTimeoutSeconds = 30)
{
    public const int CurrentSchemaVersion = 1;
}

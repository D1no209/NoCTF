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
    long ServiceDownPenalty = 0)
{
    public const int CurrentSchemaVersion = 2;
}

public sealed record AwdpChallengeConfiguration(
    int SchemaVersion,
    AwdpAchievementConfiguration? Break,
    AwdpAchievementConfiguration? Fix,
    bool RequireBreakBeforeFix,
    int MaxBreakAttempts,
    int MaxFixAttempts,
    ChallengeRuntimeTemplate? Runtime = null)
{
    public const int CurrentSchemaVersion = 1;
}

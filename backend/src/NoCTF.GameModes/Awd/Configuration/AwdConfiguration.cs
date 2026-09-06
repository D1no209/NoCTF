using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.GameModes.Flags;
using System.Text.Json.Serialization;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdConfiguration(
    int SchemaVersion,
    int HardeningDurationSeconds,
    int RoundDurationSeconds,
    AttackRewardMode AttackRewardMode,
    long AttackPoints,
    long VictimDefensePoolPoints,
    int CheckerIntervalSeconds,
    long ServiceHealthyPoints,
    long ServiceUnhealthyPenalty,
    PerTeamFlagTemplate? FlagTemplate = null)
{
    public const int CurrentSchemaVersion = 2;

    public static AwdConfiguration Default { get; } = new(
        CurrentSchemaVersion,
        HardeningDurationSeconds: 0,
        RoundDurationSeconds: 300,
        AttackRewardMode: AttackRewardMode.FixedPerAttack,
        AttackPoints: 50,
        VictimDefensePoolPoints: 100,
        CheckerIntervalSeconds: 30,
        ServiceHealthyPoints: 100,
        ServiceUnhealthyPenalty: 50);
}

[JsonConverter(typeof(AttackRewardModeJsonConverter))]
public enum AttackRewardMode
{
    FixedPerAttack,
    SplitVictimDefensePool
}

public sealed class AttackRewardModeJsonConverter()
    : JsonStringEnumConverter<AttackRewardMode>(namingPolicy: null, allowIntegerValues: false);

public sealed record AwdChallengeConfiguration(
    int SchemaVersion,
    AttackRewardMode? AttackRewardMode = null,
    long? AttackPoints = null,
    long? VictimDefensePoolPoints = null,
    int? CheckerIntervalSeconds = null,
    long? ServiceHealthyPoints = null,
    long? ServiceUnhealthyPenalty = null,
    ChallengeRuntimeTemplate? Runtime = null,
    AwdCheckerConfiguration? Checker = null,
    AwdFlagInjectionConfiguration? FlagInjection = null,
    PerTeamFlagTemplate? FlagTemplate = null,
    bool CheckerAllowRoot = false)
{
    public const int CurrentSchemaVersion = 4;
}

public sealed record AwdCheckerConfiguration(
    RunnerJobConfiguration Job,
    string? TargetServiceName = null);

public sealed record AwdFlagInjectionConfiguration(
    string Command,
    int TimeoutSeconds = 30,
    string? ServiceName = null);

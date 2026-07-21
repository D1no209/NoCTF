using NoCTF.Application.Runtime.Ports;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdConfiguration(
    int SchemaVersion,
    int RoundDurationSeconds,
    int TotalRounds,
    int FlagValidityRounds,
    long AttackPoints,
    long ServiceOnlinePoints,
    long ServiceDownPenalty,
    long VictimPenalty)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record AwdChallengeConfiguration(
    int SchemaVersion,
    string FlagFormat,
    int? MaxFlagAttempts = null,
    ChallengeRuntimeTemplate? Runtime = null)
{
    public const int CurrentSchemaVersion = 1;
}

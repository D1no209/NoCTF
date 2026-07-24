using NoCTF.Application.Runtime.Ports;

namespace NoCTF.GameModes.Koh.Configuration;

public sealed record KohConfiguration(int SchemaVersion, int PollIntervalSeconds, long ControlPointsPerInterval)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record KohChallengeConfiguration(
    int SchemaVersion,
    ChallengeRuntimeTemplate? Runtime = null,
    int? PollIntervalSeconds = null,
    long? ControlPointsPerInterval = null)
{
    public const int CurrentSchemaVersion = 1;
}

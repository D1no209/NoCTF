using NoCTF.Application.Runtime.Ports;

namespace NoCTF.GameModes.Koh.Configuration;

public sealed record KohConfiguration(int SchemaVersion, int PollIntervalSeconds, long ControlPointsPerInterval)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record KohChallengeConfiguration(
    int SchemaVersion,
    string AgentUrl,
    ChallengeRuntimeTemplate? Runtime = null,
    IReadOnlyDictionary<string, Guid>? TeamIdentifiers = null)
{
    public const int CurrentSchemaVersion = 1;
}

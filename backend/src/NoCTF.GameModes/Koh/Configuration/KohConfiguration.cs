using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.GameModes.Koh.Configuration;

public sealed record KohConfiguration(int PollIntervalSeconds, long ControlPointsPerInterval);

public sealed record KohChallengeConfiguration(
    ChallengeRuntimeTemplate? Runtime = null,
    int? PollIntervalSeconds = null,
    long? ControlPointsPerInterval = null)
;

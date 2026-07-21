using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.GameModes.Penetration.Configuration;

public sealed record PenetrationConfiguration(
    int SchemaVersion,
    CtfPointConfiguration DefaultPoints,
    IReadOnlyList<BloodReward> BloodRewards)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record PenetrationStage(
    Guid Id,
    int Number,
    string Name,
    IReadOnlyList<Guid> PrerequisiteIds,
    CtfPointConfiguration? Points,
    string? InjectionKey = null);

public sealed record PenetrationChallengeConfiguration(
    int SchemaVersion,
    IReadOnlyList<PenetrationStage> Stages,
    int? MaxFlagAttempts = null,
    ChallengeRuntimeTemplate? Runtime = null)
{
    public const int CurrentSchemaVersion = 1;
}

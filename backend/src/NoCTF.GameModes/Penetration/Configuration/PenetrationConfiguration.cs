using NoCTF.GameModes.Ctf.Configuration;

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
    CtfPointConfiguration? Points);

public sealed record PenetrationChallengeConfiguration(
    int SchemaVersion,
    IReadOnlyList<PenetrationStage> Stages,
    int? MaxFlagAttempts = null)
{
    public const int CurrentSchemaVersion = 1;
}

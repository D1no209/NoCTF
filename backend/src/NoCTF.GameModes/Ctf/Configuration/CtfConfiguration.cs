namespace NoCTF.GameModes.Ctf.Configuration;

public enum BloodRewardPolicy
{
    FixedPoints,
    InitialPointsPercentage,
    SolveTimePointsPercentage
}

public sealed record BloodReward(BloodRewardPolicy Policy, decimal Value);
public sealed record CtfPointConfiguration(int InitialPoints, int MinimumPoints, decimal DecayFactor);

public sealed record CtfConfiguration(
    int SchemaVersion,
    CtfPointConfiguration DefaultPoints,
    IReadOnlyList<BloodReward> BloodRewards)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record CtfChallengeConfiguration(
    int SchemaVersion,
    CtfPointConfiguration? Points,
    IReadOnlyList<BloodReward>? BloodRewards,
    int? MaxFlagAttempts = null)
{
    public const int CurrentSchemaVersion = 1;
}

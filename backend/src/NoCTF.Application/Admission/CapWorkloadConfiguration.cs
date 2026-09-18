using NoCTF.Domain.Platform;

namespace NoCTF.Application.Admission;

public static class CapWorkloadConfigurationRules
{
    public const int MinimumDifficulty = 1;
    public const int MaximumDifficulty = 8;
    public const int MinimumChallengeCount = 1;
    public const int MaximumChallengeCount = 500;
    public const int ChallengeSize = 32;

    public static long ExpectedHashAttempts(int difficulty, int challengeCount) =>
        challengeCount * (1L << (difficulty * 4));
}

public sealed record CapWorkloadConfiguration(
    int Difficulty,
    int ChallengeCount,
    int ChallengeSize)
{
    public long ExpectedHashAttempts =>
        CapWorkloadConfigurationRules.ExpectedHashAttempts(
            Difficulty,
            ChallengeCount);
}

public enum CapWorkloadConfigurationError
{
    ProviderNotCap,
    DifficultyInvalid,
    ChallengeCountInvalid,
    ManagementCredentialMissing,
    ManagementCredentialInvalid,
    SiteKeyNotFound,
    ProviderUnavailable,
    ConfigurationNotApplied
}

public sealed record CapWorkloadConfigurationResult(
    CapWorkloadConfiguration? Configuration = null,
    CapWorkloadConfigurationError? Error = null)
{
    public bool Succeeded => Configuration is not null && Error is null;
}

public interface ICapWorkloadConfigurationClient
{
    Task<CapWorkloadConfigurationResult> GetAsync(
        CapHumanVerificationOptions options,
        CancellationToken cancellationToken);

    Task<CapWorkloadConfigurationResult> UpdateAsync(
        CapHumanVerificationOptions options,
        int difficulty,
        int challengeCount,
        CancellationToken cancellationToken);
}

public sealed class ManageCapWorkloadConfiguration(
    IHumanVerificationConfigurationReader configurationReader,
    ICapWorkloadConfigurationClient client)
{
    public async Task<CapWorkloadConfigurationResult> GetAsync(
        CancellationToken ct = default)
    {
        var configuration = await configurationReader.GetRuntimeConfigurationAsync(ct);
        return configuration.Options.Provider == HumanVerificationProvider.Cap
            ? await client.GetAsync(configuration.Options.Cap, ct)
            : new(Error: CapWorkloadConfigurationError.ProviderNotCap);
    }

    public async Task<CapWorkloadConfigurationResult> UpdateAsync(
        int difficulty,
        int challengeCount,
        CancellationToken ct = default)
    {
        if (difficulty is < CapWorkloadConfigurationRules.MinimumDifficulty
            or > CapWorkloadConfigurationRules.MaximumDifficulty)
        {
            return new(Error: CapWorkloadConfigurationError.DifficultyInvalid);
        }
        if (challengeCount is < CapWorkloadConfigurationRules.MinimumChallengeCount
            or > CapWorkloadConfigurationRules.MaximumChallengeCount)
        {
            return new(Error: CapWorkloadConfigurationError.ChallengeCountInvalid);
        }

        var configuration = await configurationReader.GetRuntimeConfigurationAsync(ct);
        return configuration.Options.Provider == HumanVerificationProvider.Cap
            ? await client.UpdateAsync(
                configuration.Options.Cap,
                difficulty,
                challengeCount,
                ct)
            : new(Error: CapWorkloadConfigurationError.ProviderNotCap);
    }
}

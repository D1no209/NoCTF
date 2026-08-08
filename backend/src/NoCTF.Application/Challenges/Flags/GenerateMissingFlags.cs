namespace NoCTF.Application.Challenges.Flags;

public enum MissingFlagFailureCode
{
    CompetitionNotFound,
    GameModeUnsupported,
    FlagGenerationFailed
}

public sealed record MissingFlagGenerationFailure(
    Guid CompetitionChallengeId,
    Guid TeamId,
    MissingFlagFailureCode Code,
    string Description);

public interface IMissingFlagGenerator
{
    Task<IReadOnlyList<MissingFlagGenerationFailure>> GenerateAsync(
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class GenerateMissingFlags(IMissingFlagGenerator generator)
{
    public Task<IReadOnlyList<MissingFlagGenerationFailure>> ExecuteAsync(
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        generator.GenerateAsync(competitionId, now, ct);
}

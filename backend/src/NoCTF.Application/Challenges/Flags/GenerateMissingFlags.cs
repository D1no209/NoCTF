namespace NoCTF.Application.Challenges.Flags;

public sealed record MissingFlagGenerationFailure(
    Guid CompetitionChallengeId,
    Guid TeamId,
    string Code,
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

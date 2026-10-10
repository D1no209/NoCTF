namespace NoCTF.Application.Challenges.Configuration;

public enum ChallengeMaterialMutationFailure : short { ActiveExecutionScope }
public readonly record struct ChallengeMaterialScope(Guid? ChallengeId, Guid? CompetitionChallengeId);
public sealed class ChallengeMaterialMutationException(ChallengeMaterialMutationFailure failure)
    : Exception("The challenge material is in use by an active execution scope.")
{
    public ChallengeMaterialMutationFailure Failure { get; } = failure;
}
/// <summary>Checks and fences writes to mutable material; callers retain authorization and transactional ownership.</summary>
public interface IChallengeMaterialMutationGate
{
    Task RequireMutableAsync(ChallengeMaterialScope scope, CancellationToken ct);
}

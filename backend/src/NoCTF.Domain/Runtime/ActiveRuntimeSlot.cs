namespace NoCTF.Domain.Runtime;

/// <summary>
/// A provider-neutral uniqueness claim for an active runtime scope. Historical runtimes do not
/// retain a slot, so no filtered unique index is required.
/// </summary>
public sealed class ActiveRuntimeSlot
{
    public string Key { get; set; } = string.Empty;
    public Guid RuntimeInstanceId { get; set; }

    public static string CreateKey(RuntimeInstance instance)
    {
        if (instance.GameplayFactId is { } gameplayFactId)
            return $"gameplay-fact:{gameplayFactId:N}";
        if (instance.ChallengeId is { } challengeId)
            return $"template-test:{challengeId:N}";
        if (instance.CompetitionChallengeId is not { } competitionChallengeId)
            throw new InvalidOperationException(
                $"Runtime {instance.Id} has no scope from which to build an active slot.");
        var team = instance.TeamId?.ToString("N") ?? "shared";
        var key = $"competition-challenge:{competitionChallengeId:N}:team:{team}:purpose:{(short)instance.Purpose}";
        return instance.ExecutionScopeId is Guid executionScopeId ? $"{key}:scope:{executionScopeId:N}" : key;
    }
}

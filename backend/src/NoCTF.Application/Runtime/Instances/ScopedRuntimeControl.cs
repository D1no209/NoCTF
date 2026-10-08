using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Instances;

public sealed record ScopedRuntimeRequest(Guid CompetitionId, Guid CompetitionChallengeId, Guid ExecutionScopeId,
    Guid TeamId, DateTimeOffset Now, bool Reset = false);
public sealed record ScopedRuntimeResult(Guid? RuntimeInstanceId, RuntimeState? State,
    bool IsolationAvailable, RuntimeMutationFailure? Failure = null);

/// <summary>Trusted service provisioning boundary; scoped HTTP resources authorize the actor before invoking it.</summary>
public interface IScopedRuntimeControl
{
    Task<ScopedRuntimeResult> EnsureAsync(ScopedRuntimeRequest request, CancellationToken ct);
    Task StopExecutionAsync(Guid executionScopeId, DateTimeOffset now, CancellationToken ct);
}

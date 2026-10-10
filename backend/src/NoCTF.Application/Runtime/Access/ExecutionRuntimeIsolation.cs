using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Access;

public enum ExecutionIsolationStatus : short { Unverified, Verified, Unsupported, Unavailable }
public sealed record ExecutionIsolationRequest(RuntimeKind Kind, RuntimeProvider Provider, Guid ExecutionScopeId,
    Guid TeamId, Guid? RuntimeInstanceId);
public sealed record ExecutionIsolationAssessment(ExecutionIsolationStatus Status);

/// <summary>Infrastructure evidence for a controlled execution environment, distinct from its URL access mode.</summary>
public interface IExecutionRuntimeIsolation
{
    Task<ExecutionIsolationAssessment> AssessAsync(ExecutionIsolationRequest request, CancellationToken ct);
}

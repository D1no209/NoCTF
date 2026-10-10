using NoCTF.Application.Runtime.Access;

namespace NoCTF.Infrastructure.Runtime.Access;

/// <summary>No deployment or provider is certified by selecting WsrxOnly. A verified infrastructure adapter must replace this boundary.</summary>
public sealed class UnverifiedExecutionRuntimeIsolation : IExecutionRuntimeIsolation
{
    public Task<ExecutionIsolationAssessment> AssessAsync(ExecutionIsolationRequest request, CancellationToken ct) =>
        Task.FromResult(new ExecutionIsolationAssessment(ExecutionIsolationStatus.Unverified));
}

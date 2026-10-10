namespace NoCTF.Application.Runtime.Access;

/// <summary>Default fail-closed adapter for hosts without an execution-scoped feature.</summary>
public sealed class DenyExecutionScopeAccess : IExecutionScopeAccess
{
    public Task<bool> CanAccessAsync(ExecutionScopeAccessRequest request, CancellationToken cancellationToken) => Task.FromResult(false);
}

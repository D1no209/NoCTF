namespace NoCTF.Application.Runtime.Access;

public enum ExecutionScopeOperation : short { Read, Start, Reset, Stop, Extend, Prepare }
public sealed record ExecutionScopeAccessRequest(Guid ExecutionScopeId, Guid CompetitionId,
    Guid CompetitionChallengeId, Guid? TeamId, Guid ActorId, ExecutionScopeOperation Operation, DateTimeOffset Now);

/// <summary>Opaque execution authorization; the Runtime core does not know a Match or Round.</summary>
public interface IExecutionScopeAccess
{
    Task<bool> CanAccessAsync(ExecutionScopeAccessRequest request, CancellationToken cancellationToken);
}

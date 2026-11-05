namespace NoCTF.Application.GameplayFacts.PatchUploads;

public sealed record FixUploadAccessRequest(Guid CompetitionId, Guid RuntimeInstanceId, Guid ActorId,
    Guid? CompetitionChallengeId, Guid? ExecutionScopeId);
public interface IFixUploadAccessPolicy
{
    bool Supports(FixUploadAccessRequest request);
    Task<bool> CanAccessAsync(FixUploadAccessRequest request, CancellationToken ct);
}
public interface IFixUploadTargetAccess
{
    Task<bool> CanAccessAsync(FixUploadAccessRequest request, CancellationToken ct);
}
public sealed class FixUploadTargetAccess(IEnumerable<IFixUploadAccessPolicy> policies) : IFixUploadTargetAccess
{
    public Task<bool> CanAccessAsync(FixUploadAccessRequest request, CancellationToken ct)
    {
        if (request.ActorId == Guid.Empty || request.RuntimeInstanceId == Guid.Empty || request.CompetitionId == Guid.Empty
            || request.CompetitionChallengeId.HasValue == request.ExecutionScopeId.HasValue) return Task.FromResult(false);
        return policies.SingleOrDefault(policy => policy.Supports(request))?.CanAccessAsync(request, ct) ?? Task.FromResult(false);
    }
}

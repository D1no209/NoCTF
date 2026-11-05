using NoCTF.Application.GameplayFacts.PatchUploads;

namespace NoCTF.Infrastructure.GameplayFacts.PatchUploads;

public sealed class CompetitionFixUploadAccessPolicy(IPatchUploadStore targets) : IFixUploadAccessPolicy
{
    public bool Supports(FixUploadAccessRequest request) => request.CompetitionChallengeId.HasValue && !request.ExecutionScopeId.HasValue;
    public Task<bool> CanAccessAsync(FixUploadAccessRequest request, CancellationToken ct) =>
        targets.CanAccessTargetAsync(request.CompetitionId, request.CompetitionChallengeId!.Value, request.RuntimeInstanceId, request.ActorId, ct);
}

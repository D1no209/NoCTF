using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.Runtime.Access;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Resources;

public sealed class LiveSoloFixUploadAccessPolicy(NoCtfDbContext db, IExecutionScopeAccess access, TimeProvider clock) : IFixUploadAccessPolicy
{
    public bool Supports(FixUploadAccessRequest request) => request.ExecutionScopeId.HasValue && !request.CompetitionChallengeId.HasValue;
    public async Task<bool> CanAccessAsync(FixUploadAccessRequest request, CancellationToken ct)
    {
        var target = await db.Set<LiveSoloFixTargetOwnership>().AsNoTracking().Where(owner => owner.RoundQuestionId == request.ExecutionScopeId
                && owner.RuntimeInstanceId == request.RuntimeInstanceId)
            .Join(db.RuntimeInstances.AsNoTracking(), owner => owner.RuntimeInstanceId, runtime => runtime.Id, (_, runtime) => new
            { runtime.CompetitionId, runtime.CompetitionChallengeId, runtime.TeamId, runtime.Purpose, runtime.GameplayFactId }).SingleOrDefaultAsync(ct);
        if (target is not { Purpose: RuntimePurpose.ScopedFixTarget, TeamId: { } team, CompetitionChallengeId: { } challenge }
            || target.CompetitionId != request.CompetitionId) return false;
        if (target.GameplayFactId is Guid factId && await db.GameplayFacts.AnyAsync(fact => fact.Id == factId && fact.ActorUserId == request.ActorId, ct)) return true;
        if (!await db.Set<LiveSoloCompetitionChallengeRules>().AnyAsync(rules => rules.CompetitionChallengeId == challenge
                && rules.CompletionKind == LiveSoloCompletionKind.FixAndCheck, ct)) return false;
        return await access.CanAccessAsync(new(request.ExecutionScopeId!.Value, request.CompetitionId, challenge, team,
            request.ActorId, ExecutionScopeOperation.Start, clock.GetUtcNow()), ct);
    }
}

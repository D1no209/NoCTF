using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Resources;

public sealed class LiveSoloRuntimeStore(NoCtfDbContext db, IExecutionScopeAccess access, IScopedRuntimeControl runtimes, TimeProvider clock,
    IExecutionRuntimeIsolation isolation)
    : ILiveSoloRuntimeStore
{
    private sealed record Scope(Guid TeamId, Guid CompetitionChallengeId);
    private async Task<Scope?> AuthorizeAsync(LiveSoloResourceRequest request, CancellationToken ct)
    {
        if (request.ActorId == Guid.Empty) return null;
        var team = await db.Teams.AsNoTracking().Where(x => x.CompetitionId == request.CompetitionId && x.Members.Any(m => m.UserId == request.ActorId))
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        var entry = await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => x.Id == request.QuestionId && x.RoundId == request.RoundId)
            .Join(db.LiveSoloRounds.AsNoTracking().Where(x => x.MatchId == request.MatchId), x => x.RoundId, x => x.Id, (question, _) => question.CompetitionChallengeId)
            .SingleOrDefaultAsync(ct);
        if (team is not Guid teamId || entry == Guid.Empty || !await access.CanAccessAsync(new(request.QuestionId, request.CompetitionId,
            entry, teamId, request.ActorId, ExecutionScopeOperation.Read, clock.GetUtcNow()), ct)) return null;
        return new(teamId, entry);
    }
    public async Task<RuntimeInstanceView?> ReadAsync(LiveSoloResourceRequest request, CancellationToken ct)
    {
        var scope = await AuthorizeAsync(request, ct);
        return scope is null ? null : await ViewAsync(request, scope, null, ct);
    }
    private async Task<RuntimeInstanceView?> ViewAsync(LiveSoloResourceRequest request, Scope scope, Guid? id, CancellationToken ct)
    {
        var runtime = await db.RuntimeInstances.AsNoTracking().Include(x => x.AccessEndpoints)
            .Where(x => x.ExecutionScopeId == request.QuestionId && x.CompetitionId == request.CompetitionId
                && x.CompetitionChallengeId == scope.CompetitionChallengeId && x.TeamId == scope.TeamId && (id == null || x.Id == id))
            .OrderByDescending(x => x.ActiveSlot != null).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (runtime is null) return null;
        var expose = runtime.State == RuntimeState.Running && (await isolation.AssessAsync(new(runtime.RuntimeKind, runtime.RuntimeProvider,
            request.QuestionId, scope.TeamId, runtime.Id), ct)).Status == ExecutionIsolationStatus.Verified;
        return new(runtime.Id, runtime.CompetitionId, runtime.CompetitionChallengeId, null, runtime.TeamId,
            runtime.Purpose, runtime.RuntimeKind, runtime.RuntimeProvider, runtime.State, runtime.FailureCode, runtime.CreatedAt,
            runtime.RunningAt, runtime.ExpiresAt, runtime.StoppedAt, runtime.RunnerId, AccessMode: runtime.AccessMode,
            AccessEndpoints: expose ? runtime.AccessEndpoints.OrderBy(x => x.BindingIndex).Select(x =>
                new RuntimeAccessEndpointView(x.BindingIndex, null, x.TargetHost, x.TargetPort)).ToArray() : []);
    }
    public async Task<RuntimeMutationResult> MutateAsync(LiveSoloRuntimeCommand command, CancellationToken ct)
    {
        var scope = await AuthorizeAsync(command.Scope, ct);
        if (scope is null) return new(null, RuntimeMutationFailure.NotFound);
        var request = new ScopedRuntimeRequest(command.Scope.CompetitionId, scope.CompetitionChallengeId, command.Scope.QuestionId, scope.TeamId,
            clock.GetUtcNow(), command.Action == RuntimeAction.Reset, command.Scope.ActorId, command.ExpectedRuntimeInstanceId);
        var result = command.Action == RuntimeAction.Stop
            ? await runtimes.StopAsync(request, command.ExpectedRuntimeInstanceId!.Value, ct)
            : await runtimes.EnsureAsync(request, ct);
        if (result.Failure is { } failure) return new(null, failure);
        if (result.RuntimeInstanceId is not Guid runtimeId) return new(null, RuntimeMutationFailure.NotFound);
        if (await AuthorizeAsync(command.Scope, ct) is null) return new(null, RuntimeMutationFailure.NotFound);
        return new(await ViewAsync(command.Scope, scope, runtimeId, ct));
    }
}

using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Access;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Domain.Commands;

namespace NoCTF.Infrastructure.Runtime.Instances;

/// <summary>Execution-neutral provisioning. The caller owns roster/round authorization and never manipulates a provider.</summary>
public sealed class ScopedRuntimeControl(NoCtfDbContext db, IChallengeRuntimeTemplateCatalog templates,
    IRuntimePlacementPolicy placement, IPerTeamRuntimeFlagStore flags, IPostCommitMessagePublisher messages,
    IExecutionRuntimeIsolation? isolation = null, IExecutionScopeAccess? access = null, TimeProvider? clock = null,
    IRequestReplay? replay = null) : IScopedRuntimeControl
{
    public async Task<ScopedRuntimeResult> EnsureAsync(ScopedRuntimeRequest request, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var now = (clock ?? TimeProvider.System).GetUtcNow();
                if (access is not null && !await access.CanAccessAsync(new(request.ExecutionScopeId, request.CompetitionId, request.CompetitionChallengeId,
                    request.TeamId, request.ActorId ?? Guid.Empty, request.ActorId is null ? ExecutionScopeOperation.Prepare
                        : request.Reset ? ExecutionScopeOperation.Reset : ExecutionScopeOperation.Start, now), ct))
                    return new(null, null, false, RuntimeMutationFailure.NotFound);
                if (await ReplayedAsync(request, request.Reset ? RuntimeAction.Reset : RuntimeAction.Start, ct) is { } previous) return previous;
                var scope = await db.CompetitionChallenges.AsNoTracking().Where(x => x.Id == request.CompetitionChallengeId && x.CompetitionId == request.CompetitionId)
                    .Join(db.Challenges.AsNoTracking(), x => x.ChallengeId, x => x.Id, (entry, template) => new { Entry = entry, Template = template })
                    .Join(db.Competitions.AsNoTracking(), x => x.Entry.CompetitionId, x => x.Id, (x, competition) => new { x.Template, Competition = competition })
                    .SingleOrDefaultAsync(ct);
                if (scope is null || request.ExecutionScopeId == Guid.Empty || !await db.Teams.AnyAsync(x => x.Id == request.TeamId && x.CompetitionId == request.CompetitionId && !x.IsBanned, ct))
                    return new(null, null, false, RuntimeMutationFailure.NotFound);
                var template = templates.Get(scope.Template.Definition);
                if (template is null) return new(null, null, true);
                if (template.RuntimeKind != RuntimeKind.Container || template.Allocation != RuntimeAllocation.PerTeam)
                    return new(null, null, false, RuntimeMutationFailure.Unsupported);
                var current = await db.RuntimeInstances.Where(x => x.ExecutionScopeId == request.ExecutionScopeId && x.TeamId == request.TeamId
                        && x.State != RuntimeState.Stopped && x.State != RuntimeState.Failed)
                    .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
                if (request.ExpectedRuntimeInstanceId is Guid expected && current?.Id != expected)
                    return new(current?.Id, current?.State, false, RuntimeMutationFailure.Conflict);
                if (request.Reset && current is null) return new(null, null, false, RuntimeMutationFailure.InvalidState);
                if (isolation is null) return new(current?.Id, current?.State, false, RuntimeMutationFailure.Unsupported);
                var provider = current?.RuntimeProvider ?? placement.Resolve(template.RuntimeKind).Provider;
                var qualification = (await isolation.AssessAsync(
                    new(template.RuntimeKind, provider, request.ExecutionScopeId, request.TeamId, current?.Id), ct)).Status;
                if (qualification != ExecutionIsolationStatus.Verified || current is not null && current.AccessMode != RuntimeAccessMode.WsrxOnly)
                    return new(current?.Id, current?.State, false, RuntimeMutationFailure.Unsupported);
                if (current is not null && !request.Reset)
                {
                    if (request.ActorId is not null && replay is not null)
                    {
                        replay.Store(new RuntimeCommandReceipt(current.Id)); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
                    }
                    return new(current.Id, current.State, true);
                }
                var activeCount = await db.RuntimeInstances.CountAsync(x => x.CompetitionId == request.CompetitionId && x.TeamId == request.TeamId
                    && x.ActiveSlot != null && (x.State == RuntimeState.Queued || x.State == RuntimeState.Provisioning || x.State == RuntimeState.Running || x.State == RuntimeState.Stopping), ct);
                if (scope.Competition.MaxConcurrentRuntimeInstancesPerTeam > 0 && activeCount - (current is not null && request.Reset ? 1 : 0) >= scope.Competition.MaxConcurrentRuntimeInstancesPerTeam)
                    return new(null, null, true, RuntimeMutationFailure.CapacityExceeded);
                if (current is not null)
                {
                    current.State = RuntimeState.Stopping;
                    if (current.ActiveSlot is { } slot) db.Remove(slot);
                    current.ActiveSlot = null;
                    await flags.InvalidateRuntimeInstanceAsync(current.Id, now, ct);
                    await messages.PublishAsync(new StopRuntime(current.Id));
                    await db.SaveChangesAsync(ct);
                }
                var runtime = new PlayerRuntimeInstance { Id = Guid.CreateVersion7(now), CompetitionId = request.CompetitionId,
                    CompetitionChallengeId = request.CompetitionChallengeId, TeamId = request.TeamId, ExecutionScopeId = request.ExecutionScopeId,
                    RuntimeKind = template.RuntimeKind, RuntimeProvider = provider,
                    AccessMode = RuntimeAccessMode.WsrxOnly, State = RuntimeState.Queued, CreatedAt = now,
                    TrafficCaptureEnabled = scope.Competition.TrafficCaptureEnabled, TrafficCaptureLimitBytes = scope.Competition.TrafficCaptureLimitBytes };
                if (template.FlagSource == RuntimeFlagSource.PerTeam)
                    await flags.EnsureRuntimeInstanceAsync(request.CompetitionId, request.CompetitionChallengeId, request.TeamId, runtime.Id, now, ct);
                db.RuntimeInstances.Add(runtime);
                runtime.ActiveSlot = new() { Key = ActiveRuntimeSlot.CreateKey(runtime), RuntimeInstanceId = runtime.Id };
                await messages.PublishAsync(new DispatchRuntime(runtime.Id));
                if (request.ActorId is not null && replay is not null) replay.Store(new RuntimeCommandReceipt(runtime.Id));
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
                await messages.FlushCommittedMessagesAsync();
                return new(runtime.Id, runtime.State, true);
            }
            catch (Exception ex) when (!committed && attempt < 2 && (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex)))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); }
            catch (Exception ex) when (!committed && (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex)))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); return new(null, null, false, RuntimeMutationFailure.Conflict); }
            finally { if (!committed) DiscardUncommittedReplay(); }
        }
    }

    public async Task<ScopedRuntimeResult> StopAsync(ScopedRuntimeRequest request, Guid expectedRuntimeInstanceId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var now = (clock ?? TimeProvider.System).GetUtcNow();
                if (request.ActorId is null || access is null || !await access.CanAccessAsync(new(request.ExecutionScopeId, request.CompetitionId,
                    request.CompetitionChallengeId, request.TeamId, request.ActorId.Value, ExecutionScopeOperation.Stop, now), ct))
                    return new(null, null, false, RuntimeMutationFailure.NotFound);
                if (await ReplayedAsync(request with { ExpectedRuntimeInstanceId = expectedRuntimeInstanceId }, RuntimeAction.Stop, ct) is { } previous) return previous;
                var runtime = await db.RuntimeInstances.Where(x => x.Id == expectedRuntimeInstanceId && x.ExecutionScopeId == request.ExecutionScopeId
                    && x.TeamId == request.TeamId && x.CompetitionId == request.CompetitionId && x.CompetitionChallengeId == request.CompetitionChallengeId).SingleOrDefaultAsync(ct);
                if (runtime is null) return new(null, null, false, RuntimeMutationFailure.NotFound);
                if (runtime.State is not (RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running))
                    return new(runtime.Id, runtime.State, true, RuntimeMutationFailure.InvalidState);
                runtime.State = RuntimeState.Stopping; runtime.ActiveSlot = null;
                await flags.InvalidateRuntimeInstanceAsync(runtime.Id, now, ct);
                await messages.PublishAsync(new StopRuntime(runtime.Id));
                if (replay is not null) replay.Store(new RuntimeCommandReceipt(runtime.Id));
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
                await messages.FlushCommittedMessagesAsync();
                return new(runtime.Id, runtime.State, true);
            }
            catch (Exception exception) when (!committed && attempt < 2 && (exception is DbUpdateException || TransactionFailureClassifier.IsRetryable(exception)))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); }
            catch (Exception exception) when (!committed && (exception is DbUpdateException || TransactionFailureClassifier.IsRetryable(exception)))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); return new(null, null, false, RuntimeMutationFailure.Conflict); }
            finally { if (!committed) DiscardUncommittedReplay(); }
        }
    }

    private async Task<ScopedRuntimeResult?> ReplayedAsync(ScopedRuntimeRequest request, RuntimeAction action, CancellationToken ct)
    {
        if (request.ActorId is not Guid actor || replay is null) return null;
        var response = await replay.FindAsync<RuntimeCommandReceipt>(new(actor, ReplayOperation.RuntimeMutation, request.CompetitionId, request.ExecutionScopeId),
            new ScopedRuntimeReplayFingerprint(action, request.TeamId, request.ExpectedRuntimeInstanceId), ct);
        if (response is null) return null;
        var previous = await db.RuntimeInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == response.RuntimeInstanceId
            && x.ExecutionScopeId == request.ExecutionScopeId && x.TeamId == request.TeamId
            && x.CompetitionId == request.CompetitionId && x.CompetitionChallengeId == request.CompetitionChallengeId, ct);
        return previous is null ? new(null, null, false, RuntimeMutationFailure.NotFound) : new(previous.Id, previous.State, true);
    }
    private void DiscardUncommittedReplay()
    {
        foreach (var receipt in db.ChangeTracker.Entries<CommandReceipt>().Where(x => x.State == EntityState.Added).ToArray())
            receipt.State = EntityState.Detached;
    }
    public async Task StopExecutionAsync(Guid executionScopeId, DateTimeOffset now, CancellationToken ct)
    {
        var runtimes = await db.RuntimeInstances.Where(x => x.ExecutionScopeId == executionScopeId
            && (x.State == RuntimeState.Queued || x.State == RuntimeState.Provisioning || x.State == RuntimeState.Running)).ToArrayAsync(ct);
        foreach (var runtime in runtimes)
        {
            runtime.State = RuntimeState.Stopping;
            await flags.InvalidateRuntimeInstanceAsync(runtime.Id, now, ct);
            await messages.PublishAsync(new StopRuntime(runtime.Id));
        }
        await db.SaveChangesAsync(ct);
        await messages.FlushCommittedMessagesAsync();
    }
}

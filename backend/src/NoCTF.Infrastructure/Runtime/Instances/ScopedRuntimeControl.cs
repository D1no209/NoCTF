using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Access;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Instances;

/// <summary>Execution-neutral provisioning. The caller owns roster/round authorization and never manipulates a provider.</summary>
public sealed class ScopedRuntimeControl(NoCtfDbContext db, IChallengeRuntimeTemplateCatalog templates,
    IRuntimePlacementPolicy placement, IPerTeamRuntimeFlagStore flags, IPostCommitMessagePublisher messages,
    IExecutionRuntimeIsolation? isolation = null) : IScopedRuntimeControl
{
    public async Task<ScopedRuntimeResult> EnsureAsync(ScopedRuntimeRequest request, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
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
                if (isolation is null) return new(current?.Id, current?.State, false, RuntimeMutationFailure.Unsupported);
                var provider = current?.RuntimeProvider ?? placement.Resolve(template.RuntimeKind).Provider;
                var qualification = (await isolation.AssessAsync(
                    new(template.RuntimeKind, provider, request.ExecutionScopeId, request.TeamId, current?.Id), ct)).Status;
                if (qualification != ExecutionIsolationStatus.Verified || current is not null && current.AccessMode != RuntimeAccessMode.WsrxOnly)
                    return new(current?.Id, current?.State, false, RuntimeMutationFailure.Unsupported);
                if (current is not null && !request.Reset) return new(current.Id, current.State, true);
                var activeCount = await db.RuntimeInstances.CountAsync(x => x.CompetitionId == request.CompetitionId && x.TeamId == request.TeamId
                    && x.ActiveSlot != null && (x.State == RuntimeState.Queued || x.State == RuntimeState.Provisioning || x.State == RuntimeState.Running || x.State == RuntimeState.Stopping), ct);
                if (scope.Competition.MaxConcurrentRuntimeInstancesPerTeam > 0 && activeCount - (current is not null && request.Reset ? 1 : 0) >= scope.Competition.MaxConcurrentRuntimeInstancesPerTeam)
                    return new(null, null, true, RuntimeMutationFailure.CapacityExceeded);
                if (current is not null)
                {
                    current.State = RuntimeState.Stopping; current.ActiveSlot = null;
                    await flags.InvalidateRuntimeInstanceAsync(current.Id, request.Now, ct);
                    await messages.PublishAsync(new StopRuntime(current.Id));
                }
                var runtime = new PlayerRuntimeInstance { Id = Guid.CreateVersion7(request.Now), CompetitionId = request.CompetitionId,
                    CompetitionChallengeId = request.CompetitionChallengeId, TeamId = request.TeamId, ExecutionScopeId = request.ExecutionScopeId,
                    RuntimeKind = template.RuntimeKind, RuntimeProvider = provider,
                    AccessMode = RuntimeAccessMode.WsrxOnly, State = RuntimeState.Queued, CreatedAt = request.Now,
                    TrafficCaptureEnabled = scope.Competition.TrafficCaptureEnabled, TrafficCaptureLimitBytes = scope.Competition.TrafficCaptureLimitBytes };
                if (template.FlagSource == RuntimeFlagSource.PerTeam)
                    await flags.EnsureRuntimeInstanceAsync(request.CompetitionId, request.CompetitionChallengeId, request.TeamId, runtime.Id, request.Now, ct);
                db.RuntimeInstances.Add(runtime);
                runtime.ActiveSlot = new() { Key = ActiveRuntimeSlot.CreateKey(runtime), RuntimeInstanceId = runtime.Id };
                await messages.PublishAsync(new DispatchRuntime(runtime.Id));
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
                await messages.FlushCommittedMessagesAsync();
                return new(runtime.Id, runtime.State, true);
            }
            catch (Exception ex) when (!committed && attempt < 2 && (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex)))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); }
            catch (Exception ex) when (!committed && (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex)))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); return new(null, null, false, RuntimeMutationFailure.Conflict); }
        }
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

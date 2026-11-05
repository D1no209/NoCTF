using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker.Challenges.Capabilities;

public sealed class FixExecutionRecoveryHandler(NoCtfDbContext db, IPostCommitMessagePublisher messages,
    GameplayFactRecoveryCursorStore cursors, TimeProvider clock)
{
    public async Task Handle(RecoverFixExecutions message, CancellationToken ct)
    {
        var position = await cursors.ReadAsync(ct, GameplayFactRecoveryArea.FixExecutions);
        var query = db.GameplayFacts.AsNoTracking().Where(fact => fact.Kind == GameplayFactKind.FixAttempt
            && fact.FixExecution != null && fact.FixExecution.TargetKind == FixTargetKind.Isolated
            && (fact.State == GameplayFactState.Pending || fact.State == GameplayFactState.Queued || fact.State == GameplayFactState.Processing));
        var page = position.Cursor is { } cursor ? query.Where(fact => fact.UpdatedAt > cursor.UpdatedAt
            || fact.UpdatedAt == cursor.UpdatedAt && fact.Id.CompareTo(cursor.Id) > 0) : query;
        var facts = await page.OrderBy(fact => fact.UpdatedAt).ThenBy(fact => fact.Id).Take(64)
            .Select(fact => new { fact.Id, fact.UpdatedAt, fact.State, fact.FixExecution!.RuntimeInstanceId,
                fact.FixExecution.Phase, fact.FixExecution.Deadline, fact.FixExecution.WorkCompletedAt,
                RunnerId = db.RuntimeInstances.Where(runtime => runtime.Id == fact.FixExecution.RuntimeInstanceId).Select(runtime => runtime.RunnerId).SingleOrDefault() })
            .ToArrayAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var fact in facts)
        {
            if (fact.State is GameplayFactState.Pending or GameplayFactState.Queued)
                await messages.PublishAsync(new StartFixExecution(fact.Id, fact.RuntimeInstanceId));
            else if (fact.Phase == FixExecutionPhase.Finalizing && fact.WorkCompletedAt is not null)
                await messages.PublishAsync(new FinalizeFixExecution(fact.Id, fact.RuntimeInstanceId));
            else if (now >= fact.Deadline && fact.RunnerId is { } runnerId)
                await messages.PublishAsync(new ExpireFixExecution(fact.Id, fact.RuntimeInstanceId, fact.Deadline, runnerId));
        }
        await messages.FlushCommittedMessagesAsync();
        var last = facts.LastOrDefault();
        await cursors.SaveAsync(last is null ? null : new(last.UpdatedAt, last.Id), position.Revision, ct, GameplayFactRecoveryArea.FixExecutions);
    }
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker;

public sealed class PendingGameplayFactDispatchHandler(
    NoCtfDbContext db,
    IPostCommitMessagePublisher publisher,
    TimeProvider clock)
{
    public const int BatchSize = 64;

    public async Task Handle(DispatchPendingGameplayFacts message, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var queuedCutoff = now.AddSeconds(-10);
        var processingCutoff = now.AddSeconds(-60);
        var query = db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.Kind == GameplayFactKind.FlagAttempt
                || fact.Kind == GameplayFactKind.BreakAttempt
                || fact.Kind == GameplayFactKind.HintUnlock
                || fact.Kind == GameplayFactKind.ManualAdjustment
                || fact.Kind == GameplayFactKind.WriteUpUnlock)
            .Where(fact => fact.State == GameplayFactState.Queued
                    && fact.UpdatedAt <= queuedCutoff
                || fact.State == GameplayFactState.Processing
                    && fact.UpdatedAt <= processingCutoff);
        if (message.AfterId is { } afterId)
            query = query.Where(fact => fact.Id.CompareTo(afterId) > 0);

        var ids = await query.OrderBy(fact => fact.Id)
            .Take(BatchSize)
            .Select(fact => fact.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var id in ids)
            await publisher.PublishAsync(new EvaluateGameplayFact(id, Guid.CreateVersion7(now)));
        if (ids.Length == BatchSize)
            await publisher.PublishAsync(new DispatchPendingGameplayFacts(message.At, ids[^1]));
        await publisher.FlushCommittedMessagesAsync();
    }
}

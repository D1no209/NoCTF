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
                || fact.Kind == GameplayFactKind.WriteUpUnlock || fact.Kind == GameplayFactKind.TheoryPaperSubmission)
            .Where(fact => fact.State == GameplayFactState.Queued
                    && fact.UpdatedAt <= queuedCutoff
                || fact.State == GameplayFactState.Processing
                    && fact.UpdatedAt <= processingCutoff);
        // A scheduled pass is deliberately bounded. Publishing a continuation from
        // inside the pass creates a message storm when a large backlog exists.
        var candidates = await query.OrderBy(fact => fact.UpdatedAt).ThenBy(fact => fact.Id)
            .Take(BatchSize)
            .Select(fact => new { fact.Id, fact.ConcurrencyStamp })
            .ToArrayAsync(cancellationToken);
        var window = now.ToUnixTimeSeconds() / 10;
        foreach (var candidate in candidates)
            await publisher.PublishAsync(new EvaluateGameplayFact(candidate.Id, candidate.ConcurrencyStamp,
                GameplayFactDispatchPurpose.Recovery, window));
        await publisher.FlushCommittedMessagesAsync();
    }
}

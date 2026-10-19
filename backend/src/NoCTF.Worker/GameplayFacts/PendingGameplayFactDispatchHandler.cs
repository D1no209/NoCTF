using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Hosting;
using NATS.Client.Core;
using NATS.Net;
using Microsoft.Extensions.Logging;

namespace NoCTF.Worker;

public sealed class PendingGameplayFactDispatchHandler(
    NoCtfDbContext db,
    IPostCommitMessagePublisher publisher,
    TimeProvider clock,
    INatsConnection connection,
    GameplayFactRecoveryCursorStore cursors,
    ILogger<PendingGameplayFactDispatchHandler> logger)
{
    public const int BatchSize = 64;

    public async Task Handle(DispatchPendingGameplayFacts message, CancellationToken cancellationToken)
    {
        (GameplayFactRecoveryCursor? Cursor, ulong? Revision) position;
        try
        {
            var consumer = await connection.CreateJetStreamContext().GetConsumerAsync(
                NatsSubjects.GameplayStream, WorkerQueueNames.Gameplay, cancellationToken);
            if (consumer.Info.NumPending > 16 || consumer.Info.NumAckPending > 16 - (int)consumer.Info.NumPending) return;
            position = await cursors.ReadAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && exception is NatsException or TimeoutException)
        {
            logger.LogWarning("Skipped gameplay recovery because queue diagnostics or coordination are unavailable ({FailureType}).",
                exception.GetType().Name);
            return;
        }
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
        var page = position.Cursor is { } cursor
            ? query.Where(fact => fact.UpdatedAt > cursor.UpdatedAt
                || fact.UpdatedAt == cursor.UpdatedAt && fact.Id.CompareTo(cursor.Id) > 0)
            : query;
        var candidates = await page.OrderBy(fact => fact.UpdatedAt).ThenBy(fact => fact.Id)
            .Take(BatchSize)
            .Select(fact => new { fact.Id, fact.ConcurrencyStamp, fact.UpdatedAt })
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0 && position.Cursor is not null)
            candidates = await query.OrderBy(fact => fact.UpdatedAt).ThenBy(fact => fact.Id).Take(BatchSize)
                .Select(fact => new { fact.Id, fact.ConcurrencyStamp, fact.UpdatedAt }).ToArrayAsync(cancellationToken);
        var window = now.ToUnixTimeSeconds() / 10;
        foreach (var candidate in candidates)
            await publisher.PublishAsync(new EvaluateGameplayFact(candidate.Id, candidate.ConcurrencyStamp,
                GameplayFactDispatchPurpose.Recovery, window));
        await publisher.FlushCommittedMessagesAsync();
        var last = candidates.LastOrDefault();
        await cursors.SaveAsync(last is null ? null : new(last.UpdatedAt, last.Id), position.Revision, cancellationToken);
    }
}

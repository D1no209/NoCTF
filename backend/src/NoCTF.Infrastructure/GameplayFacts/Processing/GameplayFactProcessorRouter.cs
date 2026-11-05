using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Gameplay;
using NoCTF.Application.Observability;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

/// <summary>Routes the specified fact and passes the same read-only version to its evaluator.</summary>
public sealed class GameplayFactProcessorRouter(NoCtfDbContext db, GameplayFactProcessor ordinary,
    ILiveSoloMatchStore scoped, TimeProvider clock, IPostCommitMessagePublisher messages) : IGameplayFactProcessor
{
    public async Task ProcessAsync(Guid factId, Guid expectedConcurrencyStamp, CancellationToken ct)
    {
        var scope = await db.GameplayFacts.AsNoTracking()
            .Include(fact => fact.Awd)
            .Where(fact => fact.Id == factId)
            .Select(fact => new
            {
                Fact = fact,
                RoundId = db.LiveSoloSubmissions.Where(item => item.GameplayFactId == fact.Id)
                    .Select(item => (Guid?)item.RoundId).SingleOrDefault()
            })
            .SingleOrDefaultAsync(ct);
        if (scope is null)
        {
            NoCtfTelemetry.RecordGameplayFactDelivery(GameplayFactDeliveryOutcome.Missing);
            return;
        }
        if (scope.Fact.State is not (GameplayFactState.Queued or GameplayFactState.Processing))
        {
            NoCtfTelemetry.RecordGameplayFactDelivery(GameplayFactDeliveryOutcome.AlreadyFinished);
            return;
        }
        if (scope.Fact.ConcurrencyStamp != expectedConcurrencyStamp)
        {
            NoCtfTelemetry.RecordGameplayFactDelivery(GameplayFactDeliveryOutcome.Stale);
            return;
        }
        if (scope.Fact.Kind == GameplayFactKind.WriteUpUnlock)
        {
            var stamp = Guid.NewGuid();
            var now = clock.GetUtcNow();
            var completed = await db.GameplayFacts.Where(item => item.Id == factId
                    && item.ConcurrencyStamp == expectedConcurrencyStamp && item.State == GameplayFactState.Queued
                    && (item.Kind == GameplayFactKind.WriteUpUnlock
                        && db.WriteUpUnlockReceipts.Any(receipt => receipt.GameplayFactId == item.Id)
                        ))
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.State, GameplayFactState.Completed)
                    .SetProperty(item => item.UpdatedAt, now)
                    .SetProperty(item => item.ConcurrencyStamp, stamp), ct);
            if (completed == 1)
            {
                await messages.PublishAsync(new GameplayFactStateChanged(factId, GameplayFactState.Completed, stamp));
                await messages.FlushCommittedMessagesAsync();
            }
            return;
        }
        if (scope.RoundId is Guid roundId) await scoped.ResolveAsync(roundId, clock.GetUtcNow(), ct);
        else await ordinary.ProcessAsync(scope.Fact, ct);
    }
}

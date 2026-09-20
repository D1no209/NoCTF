using System.Collections.Concurrent;
using NoCTF.Application.Competitions.Webhooks;

namespace NoCTF.Infrastructure.Competitions.Webhooks;

public sealed class DevelopmentCompetitionWebhookTestStatusStore(
    TimeProvider timeProvider) : ICompetitionWebhookTestStatusStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<Guid, Entry> entries = new();

    public Task CreateAsync(
        CompetitionWebhookTestStatus status,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!entries.TryAdd(status.DeliveryId, new(status, timeProvider.GetUtcNow() + Lifetime)))
            throw new InvalidOperationException("Webhook test delivery already exists.");
        return Task.CompletedTask;
    }

    public Task<CompetitionWebhookTestStatus?> GetAsync(
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!entries.TryGetValue(deliveryId, out var entry))
            return Task.FromResult<CompetitionWebhookTestStatus?>(null);
        if (entry.ExpiresAt <= timeProvider.GetUtcNow())
        {
            entries.TryRemove(deliveryId, out _);
            return Task.FromResult<CompetitionWebhookTestStatus?>(null);
        }
        return Task.FromResult<CompetitionWebhookTestStatus?>(entry.Status);
    }

    public async Task CompleteAsync(
        Guid deliveryId,
        CompetitionWebhookTestState state,
        DateTimeOffset completedAt,
        CompetitionWebhookTestFailureCode? failureCode,
        CancellationToken cancellationToken)
    {
        var current = await GetAsync(deliveryId, cancellationToken);
        if (current is null)
            return;
        entries[deliveryId] = new(current with
        {
            State = state,
            CompletedAt = completedAt,
            FailureCode = failureCode
        }, timeProvider.GetUtcNow() + Lifetime);
    }

    private sealed record Entry(
        CompetitionWebhookTestStatus Status,
        DateTimeOffset ExpiresAt);
}

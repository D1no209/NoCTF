using System.Text.Json;
using NoCTF.Application.Competitions.Webhooks;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Competitions.Webhooks;

public sealed class RedisCompetitionWebhookTestStatusStore(
    IConnectionMultiplexer redis) : ICompetitionWebhookTestStatusStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task CreateAsync(
        CompetitionWebhookTestStatus status,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var created = await redis.GetDatabase().StringSetAsync(
            Key(status.DeliveryId),
            JsonSerializer.Serialize(status, JsonOptions),
            Lifetime,
            When.NotExists);
        if (!created)
            throw new InvalidOperationException("Webhook test delivery already exists.");
    }

    public async Task<CompetitionWebhookTestStatus?> GetAsync(
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await redis.GetDatabase().StringGetAsync(Key(deliveryId));
        return value.HasValue
            ? JsonSerializer.Deserialize<CompetitionWebhookTestStatus>(value.ToString(), JsonOptions)
            : null;
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
        await redis.GetDatabase().StringSetAsync(
            Key(deliveryId),
            JsonSerializer.Serialize(current with
            {
                State = state,
                CompletedAt = completedAt,
                FailureCode = failureCode
            }, JsonOptions),
            Lifetime);
    }

    private static string Key(Guid id) => $"noctf:webhook-test:v1:{id:N}";
}

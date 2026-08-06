using Microsoft.Extensions.Configuration;
using NoCTF.Application.Scoring.Leaderboard;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class FusionLeaderboardSubscriptionRegistry(
    IFusionCache cache,
    IConfiguration configuration) : ILeaderboardSubscriptionRegistry
{
    private const int DefaultSubscriberTtlSeconds = 90;
    private readonly TimeSpan ttl = TimeSpan.FromSeconds(Math.Max(
        1,
        configuration.GetValue(
            "Leaderboard:SubscriberTtlSeconds",
            DefaultSubscriberTtlSeconds)));

    public async Task<bool> TouchAsync(
        Guid competitionId,
        string subscriberId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        var key = ActiveKey(competitionId);
        var wasActive = await cache.GetOrDefaultAsync(
            key,
            false,
            token: cancellationToken);
        await cache.SetAsync(
            key,
            true,
            options => options.SetDuration(ttl),
            token: cancellationToken);
        return !wasActive;
    }

    public Task<bool> HasActiveAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        cache.GetOrDefaultAsync(
            ActiveKey(competitionId),
            false,
            token: cancellationToken).AsTask();

    public Task RemoveAsync(
        Guid competitionId,
        string subscriberId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        cancellationToken.ThrowIfCancellationRequested();
        // Keep the shared marker until its short TTL expires. Removing it here could
        // hide subscribers connected to another horizontally scaled API process.
        return Task.CompletedTask;
    }

    private static string ActiveKey(Guid competitionId) =>
        $"leaderboard:v3:{competitionId:N}:subscribers-active";
}

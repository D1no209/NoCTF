using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Caching;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Competitions.Management;

public sealed class CompetitionReadModelCache(IFusionCacheProvider caches)
{
    private const string ListKey = "competitions:public:list";
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.ReadModels);

    public Task<CompetitionView?> GetAsync(
        Guid competitionId,
        Func<CancellationToken, Task<CompetitionView?>> factory,
        CancellationToken cancellationToken) =>
        cache.GetOrSetAsync<CompetitionView?>(
            DetailKey(competitionId),
            (_, token) => factory(token),
            token: cancellationToken).AsTask();

    public async Task<IReadOnlyList<CompetitionView>> ListAsync(
        Func<CancellationToken, Task<CompetitionView[]>> factory,
        CancellationToken cancellationToken) =>
        await cache.GetOrSetAsync<CompetitionView[]>(
            ListKey,
            (_, token) => factory(token),
            token: cancellationToken);

    public Task<CompetitionAccessMode?> GetAccessModeAsync(
        Guid competitionId,
        Func<CancellationToken, Task<CompetitionAccessMode?>> factory,
        CancellationToken cancellationToken) =>
        cache.GetOrSetAsync<CompetitionAccessMode?>(
            AccessModeKey(competitionId),
            (_, token) => factory(token),
            token: cancellationToken).AsTask();

    public async Task InvalidateAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(DetailKey(competitionId), token: cancellationToken);
        await cache.RemoveAsync(ListKey, token: cancellationToken);
        await cache.RemoveAsync(AccessModeKey(competitionId), token: cancellationToken);
    }

    private static string DetailKey(Guid competitionId) =>
        $"competition:{competitionId:N}:public";

    private static string AccessModeKey(Guid competitionId) =>
        $"competition:{competitionId:N}:access-mode";
}

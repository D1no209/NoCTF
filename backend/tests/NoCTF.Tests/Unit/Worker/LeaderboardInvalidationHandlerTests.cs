using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Worker;

namespace NoCTF.Tests.Unit.Worker;

public sealed class LeaderboardInvalidationHandlerTests
{
    [Test]
    [Arguments(true, 1)]
    [Arguments(false, 0)]
    public async Task Invalidation_only_projects_for_active_subscribers(
        bool hasActiveSubscriber,
        int expectedRefreshes)
    {
        var competitionId = Guid.CreateVersion7();
        var cache = new RecordingCache();

        await BackendMessageHandlers.Handle(
            new InvalidateLeaderboard(competitionId),
            cache,
            new SubscriptionRegistry(hasActiveSubscriber),
            CancellationToken.None);

        await Assert.That(cache.InvalidatedCompetitionIds)
            .IsEquivalentTo([competitionId]);
        await Assert.That(cache.RefreshedCompetitionIds).Count()
            .IsEqualTo(expectedRefreshes);
    }

    private sealed class RecordingCache : ILeaderboardCache
    {
        public List<Guid> InvalidatedCompetitionIds { get; } = [];
        public List<Guid> RefreshedCompetitionIds { get; } = [];

        public Task<LeaderboardResponse?> GetAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LeaderboardResponse?>(null);

        public Task RefreshAsync(
            Guid competitionId,
            CancellationToken cancellationToken)
        {
            RefreshedCompetitionIds.Add(competitionId);
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(
            Guid competitionId,
            CancellationToken cancellationToken)
        {
            InvalidatedCompetitionIds.Add(competitionId);
            return Task.CompletedTask;
        }
    }

    private sealed class SubscriptionRegistry(bool hasActiveSubscriber)
        : ILeaderboardSubscriptionRegistry
    {
        public Task<bool> TouchAsync(
            Guid competitionId,
            string subscriberId,
            CancellationToken cancellationToken) =>
            Task.FromResult(hasActiveSubscriber);

        public Task<bool> HasActiveAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(hasActiveSubscriber);

        public Task RemoveAsync(
            Guid competitionId,
            string subscriberId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}

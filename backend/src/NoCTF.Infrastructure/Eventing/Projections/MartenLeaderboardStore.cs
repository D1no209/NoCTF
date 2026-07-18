using System.Text.Json;
using Marten;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Scoring.Ports;
using NoCTF.Infrastructure.Eventing.ProjectionCheckpoints;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Eventing.Projections;

public sealed class MartenLeaderboardStore(
    IDocumentSession session,
    IConnectionMultiplexer? redis = null) : ILeaderboardStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LeaderboardSnapshot?> GetAuthoritativeAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        var document = await session.LoadAsync<LeaderboardDocument>(competitionId, cancellationToken);
        return document is null
            ? null
            : new(document.CompetitionId, document.ProjectionVersion, document.GeneratedAt, document.Entries);
    }

    public async Task PublishCacheAsync(LeaderboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (redis is null) return;
        var payload = JsonSerializer.Serialize(snapshot, JsonOptions);
        await redis.GetDatabase().StringSetAsync(
            $"noctf:leaderboard:{snapshot.CompetitionId:N}",
            payload,
            TimeSpan.FromMinutes(1));
    }

    public async Task<IReadOnlyList<Guid>> GetDirtyCompetitionsAsync(CancellationToken cancellationToken) =>
        await session.Query<ScoringProjectionCheckpoint>()
            .Where(checkpoint => checkpoint.IsDirty)
            .Select(checkpoint => checkpoint.CompetitionId)
            .ToListAsync(cancellationToken);

    public async Task MarkCleanAsync(Guid competitionId, long projectionVersion, CancellationToken cancellationToken)
    {
        var checkpoint = await session.LoadAsync<ScoringProjectionCheckpoint>(competitionId, cancellationToken);
        if (checkpoint is null || checkpoint.ProjectionVersion != projectionVersion) return;
        checkpoint.IsDirty = false;
        checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(checkpoint);
        await session.SaveChangesAsync(cancellationToken);
    }
}

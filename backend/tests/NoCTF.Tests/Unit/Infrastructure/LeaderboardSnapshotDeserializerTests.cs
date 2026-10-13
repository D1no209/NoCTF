using System.Text.Json;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Scoring.Leaderboard;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class LeaderboardSnapshotDeserializerTests
{
    [Test]
    public async Task Concurrent_reads_share_only_the_exact_validated_generation()
    {
        using var decoder = new LeaderboardSnapshotDeserializer();
        var id = Guid.NewGuid();
        var first = Payload(id, 7);
        var values = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => decoder.ReadAsync(id, first, CancellationToken.None).AsTask()));
        foreach (var value in values)
            await Assert.That(value).IsSameReferenceAs(values[0]);
        var next = await decoder.ReadAsync(id, Payload(id, 8), CancellationToken.None);
        await Assert.That(next.Scoreboard.Snapshot.Version).IsEqualTo(8L);
        await Assert.That(next).IsNotSameReferenceAs(values[0]);
        // Even a producer reusing a fence cannot make a changed payload reuse an old graph.
        var changed = Payload(id, 8, DateTimeOffset.UnixEpoch.AddSeconds(1));
        var replacement = await decoder.ReadAsync(id, changed, CancellationToken.None);
        await Assert.That(replacement.Scoreboard.Snapshot.GeneratedAt).IsEqualTo(DateTimeOffset.UnixEpoch.AddSeconds(1));
    }

    [Test]
    public async Task Wrong_scope_or_version_is_rejected_and_oversized_payloads_are_not_retained()
    {
        using var decoder = new LeaderboardSnapshotDeserializer(1);
        var id = Guid.NewGuid();
        var payload = Payload(id, 3);
        var first = await decoder.ReadAsync(id, payload, CancellationToken.None);
        var second = await decoder.ReadAsync(id, payload, CancellationToken.None);
        await Assert.That(second).IsNotSameReferenceAs(first);
        await Assert.That(async () => await decoder.ReadAsync(Guid.NewGuid(), payload, CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await decoder.ReadAsync(id, payload with { Fence = 4 }, CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    private static LeaderboardFencedPayload Payload(Guid id, long version, DateTimeOffset? at = null)
    {
        var projection = new CachedScoreboardProjection(new ScoreboardProjection(
            new(id, 1, []), new(id, GameMode.Ctf, 1, 1, [], []),
            new(id, version, 1, at ?? DateTimeOffset.UnixEpoch, null, [], [])));
        return new(version, JsonSerializer.Serialize(projection,
            CachedScoreboardJsonContext.Default.CachedScoreboardProjection));
    }
}

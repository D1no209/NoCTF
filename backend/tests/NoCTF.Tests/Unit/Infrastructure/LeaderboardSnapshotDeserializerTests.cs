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
            .Select(_ => Task.Run(async () => await decoder.ReadAsync(id, first, CancellationToken.None))));
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
    public async Task Current_generation_is_retained_when_the_budget_only_fits_one_generation()
    {
        var id = Guid.NewGuid();
        var first = Payload(id, 7);
        using var decoder = new LeaderboardSnapshotDeserializer(first.Payload.Length + 64);
        _ = await decoder.ReadAsync(id, first, CancellationToken.None);
        var nextPayload = Payload(id, 8);
        var next = await decoder.ReadAsync(id, nextPayload, CancellationToken.None);
        await Assert.That(await decoder.ReadAsync(id, nextPayload, CancellationToken.None)).IsSameReferenceAs(next);
        var earlier = await decoder.ReadAsync(id, first, CancellationToken.None);
        await Assert.That(earlier.Scoreboard.Snapshot.Version).IsEqualTo(7L);
        await Assert.That(await decoder.ReadAsync(id, nextPayload, CancellationToken.None)).IsSameReferenceAs(next);
    }

    [Test]
    public async Task Larger_late_payload_cannot_evict_the_newer_decoded_version()
    {
        var id = Guid.NewGuid();
        var current = Payload(id, 8);
        var old = Payload(id, 7);
        old = old with { Payload = old.Payload + new string(' ', old.Payload.Length * 2) };
        using var decoder = new LeaderboardSnapshotDeserializer(old.Payload.Length * 2);
        var installed = await decoder.ReadAsync(id, current, CancellationToken.None);
        var earlier = await decoder.ReadAsync(id, old, CancellationToken.None);
        await Assert.That(earlier.Scoreboard.Snapshot.Version).IsEqualTo(7L);
        await Assert.That(await decoder.ReadAsync(id, current, CancellationToken.None)).IsSameReferenceAs(installed);
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

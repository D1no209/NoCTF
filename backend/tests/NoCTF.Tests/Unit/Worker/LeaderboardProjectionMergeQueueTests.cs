using NSubstitute;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Worker;

namespace NoCTF.Tests.Unit.Worker;

public sealed class LeaderboardProjectionMergeQueueTests
{
    [Test]
    public async Task Repeated_events_for_one_competition_merge_into_one_fixed_window()
    {
        var queue = new LeaderboardProjectionMergeQueue();
        var competitionId = Guid.CreateVersion7();
        var startedAt = DateTimeOffset.Parse("2026-08-24T00:00:00Z");

        var first = queue.Enqueue(competitionId, startedAt);
        var repeated = queue.Enqueue(competitionId, startedAt.AddMilliseconds(400));
        var beforeWindow = queue.TakeDue(startedAt.AddMilliseconds(499));
        var atWindow = queue.TakeDue(startedAt.AddMilliseconds(500));

        await Assert.That(first).IsTrue();
        await Assert.That(repeated).IsFalse();
        await Assert.That(beforeWindow).IsEmpty();
        await Assert.That(atWindow).IsEquivalentTo([competitionId]);
        await Assert.That(queue.TakeDue(startedAt.AddDays(1))).IsEmpty();
    }

    [Test]
    public async Task Completed_window_allows_a_later_event_to_start_a_new_window()
    {
        var queue = new LeaderboardProjectionMergeQueue();
        var competitionId = Guid.CreateVersion7();
        var startedAt = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        queue.Enqueue(competitionId, startedAt);
        _ = queue.TakeDue(startedAt.AddMilliseconds(500));

        var startedAgain = queue.Enqueue(competitionId, startedAt.AddSeconds(1));

        await Assert.That(startedAgain).IsTrue();
        await Assert.That(queue.TakeDue(startedAt.AddMilliseconds(1499))).IsEmpty();
        await Assert.That(queue.TakeDue(startedAt.AddMilliseconds(1500)))
            .IsEquivalentTo([competitionId]);
    }

    [Test]
    public async Task Relevant_event_invalidates_immediately_and_enters_merge_window()
    {
        var leaderboard = Substitute.For<ILeaderboardCache>();
        var queue = new LeaderboardProjectionMergeQueue();
        var now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        var handler = new CompetitionEventLeaderboardMessageHandler(
            leaderboard,
            queue,
            new FixedTimeProvider(now));
        var competitionId = Guid.CreateVersion7();

        await handler.Handle(
            CreateEvent(competitionId, CompetitionEventKind.ScoringRecorded),
            CancellationToken.None);

        await leaderboard.Received(1).InvalidateAsync(competitionId, CancellationToken.None);
        await Assert.That(queue.TakeDue(now.AddMilliseconds(499))).IsEmpty();
        await Assert.That(queue.TakeDue(now.AddMilliseconds(500)))
            .IsEquivalentTo([competitionId]);
    }

    [Test]
    public async Task Irrelevant_event_neither_invalidates_nor_enters_merge_window()
    {
        var leaderboard = Substitute.For<ILeaderboardCache>();
        var queue = new LeaderboardProjectionMergeQueue();
        var handler = new CompetitionEventLeaderboardMessageHandler(
            leaderboard,
            queue,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-08-24T00:00:00Z")));

        await handler.Handle(
            CreateEvent(Guid.CreateVersion7(), CompetitionEventKind.QuestionReplied),
            CancellationToken.None);

        await leaderboard.DidNotReceiveWithAnyArgs().InvalidateAsync(default, default);
        await Assert.That(queue.TakeDue(DateTimeOffset.MaxValue)).IsEmpty();
    }

    private static CompetitionEventCommitted CreateEvent(
        Guid competitionId,
        CompetitionEventKind kind) => new(
        competitionId,
        Guid.CreateVersion7(),
        kind,
        CompetitionEventLevel.Information,
        DateTimeOffset.Parse("2026-08-24T00:00:00Z"));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

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
    [Arguments(CompetitionEventKind.ScoringRecorded, false)]
    // Track changes change who may see teams, so they also rotate the scoreboard access revision.
    [Arguments(CompetitionEventKind.TrackConfigurationUpdated, true)]
    [Arguments(CompetitionEventKind.TeamTrackChanged, true)]
    public async Task Relevant_event_invalidates_immediately_and_enters_merge_window(
        CompetitionEventKind kind, bool changesAccess)
    {
        var leaderboard = Substitute.For<ILeaderboardCache>();
        var queue = new LeaderboardProjectionMergeQueue();
        var now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        var applicability = Substitute.For<ICompetitionEventApplicabilityReader>();
        applicability.HasOrdinaryScoreboardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CompetitionEventLeaderboardMessageHandler(
            leaderboard,
            queue,
            new FixedTimeProvider(now), applicability);
        var competitionId = Guid.CreateVersion7();

        await handler.Handle(
            CreateEvent(competitionId, kind),
            CancellationToken.None);

        await leaderboard.Received(changesAccess ? 0 : 1).InvalidateAsync(competitionId, CancellationToken.None);
        await leaderboard.Received(changesAccess ? 1 : 0).InvalidateAccessAsync(competitionId, CancellationToken.None);
        await Assert.That(queue.TakeDue(now.AddMilliseconds(499))).IsEmpty();
        await Assert.That(queue.TakeDue(now.AddMilliseconds(500)))
            .IsEquivalentTo([competitionId]);
    }

    [Test]
    [Arguments(CompetitionEventKind.QuestionReplied)]
    [Arguments(CompetitionEventKind.TrackRegistrationPolicyUpdated)]
    public async Task Irrelevant_event_neither_invalidates_nor_enters_merge_window(
        CompetitionEventKind kind)
    {
        var leaderboard = Substitute.For<ILeaderboardCache>();
        var queue = new LeaderboardProjectionMergeQueue();
        var handler = new CompetitionEventLeaderboardMessageHandler(
            leaderboard,
            queue,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-08-24T00:00:00Z")), Substitute.For<ICompetitionEventApplicabilityReader>());

        await handler.Handle(
            CreateEvent(Guid.CreateVersion7(), kind),
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

    [Test]
    public async Task Independent_mode_events_do_not_enter_the_ordinary_scoreboard_queue()
    {
        var leaderboard = Substitute.For<ILeaderboardCache>(); var queue = new LeaderboardProjectionMergeQueue();
        var applicability = Substitute.For<ICompetitionEventApplicabilityReader>();
        applicability.HasOrdinaryScoreboardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new CompetitionEventLeaderboardMessageHandler(leaderboard, queue, TimeProvider.System, applicability);
        await handler.Handle(CreateEvent(Guid.NewGuid(), CompetitionEventKind.ChallengeUpdated), CancellationToken.None);
        await leaderboard.DidNotReceiveWithAnyArgs().InvalidateAsync(default, default);
        await Assert.That(queue.TakeDue(DateTimeOffset.MaxValue)).IsEmpty();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

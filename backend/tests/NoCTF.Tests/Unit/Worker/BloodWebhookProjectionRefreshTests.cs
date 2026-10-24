using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Worker;

namespace NoCTF.Tests.Unit.Worker;

public sealed class BloodWebhookProjectionRefreshTests
{
    [Test]
    [Arguments(CompetitionEventKind.FirstBloodAwarded)]
    [Arguments(CompetitionEventKind.SecondBloodAwarded)]
    [Arguments(CompetitionEventKind.ThirdBloodAwarded)]
    public async Task Derived_blood_event_requests_a_projection_that_can_cover_its_outbox_sequence(
        CompetitionEventKind kind)
    {
        var competitionId = Guid.NewGuid();
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var cache = Substitute.For<ILeaderboardCache>();
        var applicability = Substitute.For<ICompetitionEventApplicabilityReader>();
        applicability.HasOrdinaryScoreboardAsync(competitionId, Arg.Any<CancellationToken>())
            .Returns(true);
        var queue = new LeaderboardProjectionMergeQueue();
        var handler = new CompetitionEventLeaderboardMessageHandler(cache, queue, clock, applicability);
        var message = new CompetitionEventCommitted(competitionId, Guid.NewGuid(), kind,
            CompetitionEventLevel.Information, clock.GetUtcNow(), Visibility: CompetitionEventVisibility.Public);

        await handler.Handle(message, CancellationToken.None);

        await cache.Received(1).InvalidateAsync(competitionId, Arg.Any<CancellationToken>());
        await Assert.That(queue.TakeDue(clock.GetUtcNow())).IsEmpty();
        clock.Advance(LeaderboardProjectionMergeQueue.MergeWindow);
        await Assert.That(queue.TakeDue(clock.GetUtcNow())).Contains(competitionId);
    }
}

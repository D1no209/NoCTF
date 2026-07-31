using NSubstitute;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Messaging;
using Wolverine;

namespace NoCTF.Tests.Unit.Application;

public sealed class WolverineBackendMessagePublisherTests
{
    [Test]
    public async Task Rebuild_competition_sends_a_leaderboard_invalidation()
    {
        var bus = Substitute.For<IMessageBus>();
        var publisher = new WolverineBackendMessagePublisher(bus);
        var competitionId = Guid.CreateVersion7();

        await publisher.RebuildCompetitionAsync(competitionId, CancellationToken.None);

        var messages = bus.ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .OfType<InvalidateLeaderboard>()
            .ToList();
        await Assert.That(messages).Count().IsEqualTo(1);
        await Assert.That(messages[0].CompetitionId).IsEqualTo(competitionId);
    }
}

using NSubstitute;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Worker;
using Wolverine;

namespace NoCTF.Tests.Unit.Worker;

public sealed class CompetitionEventWebhookMessageHandlerTests
{
    [Test]
    [Arguments(CompetitionEventKind.ChallengePublished, CompetitionEventVisibility.Public, true)]
    [Arguments(CompetitionEventKind.ChallengePublished, CompetitionEventVisibility.Team, false)]
    [Arguments(CompetitionEventKind.GameplayFactReceived, CompetitionEventVisibility.Team, false)]
    [Arguments(CompetitionEventKind.GameplayFactAdjudicated, CompetitionEventVisibility.Public, false)]
    public async Task Only_public_webhook_events_are_dispatched(
        CompetitionEventKind kind, CompetitionEventVisibility visibility, bool dispatched)
    {
        var bus = Substitute.For<IMessageBus>();
        var message = new CompetitionEventCommitted(Guid.CreateVersion7(), Guid.CreateVersion7(), kind,
            CompetitionEventLevel.Information, DateTimeOffset.UnixEpoch, Visibility: visibility);

        await new CompetitionEventWebhookMessageHandler(bus).Handle(message);

        await bus.Received(dispatched ? 1 : 0).PublishAsync(
            Arg.Is<DispatchCompetitionWebhooks>(item => item != null && item.EventId == message.EventId),
            Arg.Any<DeliveryOptions?>());
    }
}

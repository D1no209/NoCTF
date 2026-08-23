using Microsoft.AspNetCore.SignalR;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Endpoints.Competitions.Events;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.SignalR.Hubs;
using NoCTF.API.SignalR.Publishing;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class SignalRPublisherTests
{
    [Test]
    public async Task Gameplay_fact_updates_are_mapped_and_sent_to_the_user()
    {
        var harness = CreateHarness();
        var userId = Guid.CreateVersion7();
        var view = new GameplayFactStatusView(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            GameplayFactKind.FlagAttempt,
            GameplayFactState.Completed,
            GameplayFactResult.Correct,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(1));

        await new SignalRGameplayFactStatePublisher(harness.Context)
            .PublishAsync(userId, view, CancellationToken.None);

        _ = harness.Clients.Received(1).User(userId.ToString());
        await harness.Client.Received(1).GameplayFactStateChanged(
            Arg.Is<GameplayFactStatusResponse>(notification =>
                notification != null
                && notification.GameplayFactId == view.GameplayFactId
                && notification.Kind == GameplayFactKindProtocol.FlagAttempt
                && notification.State == GameplayFactStateProtocol.Completed
                && notification.Result == GameplayFactResultProtocol.Correct),
            CancellationToken.None);
    }

    [Test]
    public async Task Lifecycle_updates_are_mapped_and_sent_to_the_competition_group()
    {
        var harness = CreateHarness();
        var competitionId = Guid.CreateVersion7();

        await new SignalRCompetitionLifecyclePublisher(harness.Context).PublishAsync(
            competitionId,
            CompetitionStatus.Running,
            CompetitionStatus.Finished,
            DateTimeOffset.UnixEpoch,
            CancellationToken.None);

        _ = harness.Clients.Received(1).Group($"competition:{competitionId:N}");
        await harness.Client.Received(1).CompetitionLifecycleChanged(
            Arg.Is<CompetitionLifecycleChangedNotification>(notification =>
                notification != null
                && notification.CompetitionId == competitionId
                && notification.From == CompetitionStatusProtocol.Running
                && notification.To == CompetitionStatusProtocol.Finished),
            CancellationToken.None);
    }

    [Test]
    public async Task Competition_events_are_mapped_and_sent_to_the_competition_group()
    {
        var harness = CreateHarness();
        var competitionId = Guid.CreateVersion7();
        var message = new CompetitionEventCommitted(
            competitionId,
            Guid.CreateVersion7(),
            CompetitionEventKind.ScoringRecorded,
            CompetitionEventLevel.Information,
            DateTimeOffset.UnixEpoch);

        await new LocalCompetitionEventMessageHandler(harness.Context)
            .Handle(message, CancellationToken.None);

        _ = harness.Clients.Received(1).Group($"competition:{competitionId:N}");
        await harness.Client.Received(1).CompetitionEventChanged(
            Arg.Is<CompetitionEventChangedNotification>(notification =>
                notification != null
                && notification.EventId == message.EventId
                && notification.Kind == CompetitionEventKindProtocol.ScoringRecorded
                && notification.Level == CompetitionEventLevelProtocol.Information),
            CancellationToken.None);
    }

    private static PublisherHarness CreateHarness()
    {
        var context = Substitute.For<IHubContext<CompetitionHub, ICompetitionHubClient>>();
        var clients = Substitute.For<IHubClients<ICompetitionHubClient>>();
        var client = Substitute.For<ICompetitionHubClient>();
        context.Clients.Returns(clients);
        clients.User(Arg.Any<string>()).Returns(client);
        clients.Group(Arg.Any<string>()).Returns(client);
        return new(context, clients, client);
    }

    private sealed record PublisherHarness(
        IHubContext<CompetitionHub, ICompetitionHubClient> Context,
        IHubClients<ICompetitionHubClient> Clients,
        ICompetitionHubClient Client);
}

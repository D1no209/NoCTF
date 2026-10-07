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

        await new SignalRGameplayFactStatePublisher(harness.Context, MfaHubTestSupport.Guard(("connection", userId, MfaHubKind.Competition)))
            .PublishAsync(userId, view, CancellationToken.None);

        _ = harness.Clients.Received(1).Clients(Arg.Is<IReadOnlyList<string>>(value => value != null && value.Contains("connection")));
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

        await new SignalRCompetitionLifecyclePublisher(harness.Audiences).PublishAsync(
            competitionId,
            CompetitionStatus.Running,
            CompetitionStatus.Finished,
            DateTimeOffset.UnixEpoch,
            CancellationToken.None);

        await harness.Audiences.Received(1).CurrentAsync(
            competitionId,
            CancellationToken.None);
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

        await new LocalCompetitionEventMessageHandler(
                harness.Audiences,
                harness.Coordinator)
            .Handle(message, CancellationToken.None);

        await harness.Audiences.Received(1).CurrentAsync(
            competitionId,
            CancellationToken.None);
        await harness.Client.Received(1).CompetitionEventChanged(
            Arg.Is<CompetitionEventChangedNotification>(notification =>
                notification != null
                && notification.EventId == message.EventId
                && notification.Kind == CompetitionEventKindProtocol.ScoringRecorded
                && notification.Level == CompetitionEventLevelProtocol.Information),
            CancellationToken.None);
    }

    [Test]
    public async Task Audience_change_events_notify_all_known_groups_and_trigger_reauthorization()
    {
        var harness = CreateHarness();
        var competitionId = Guid.CreateVersion7();
        var message = new CompetitionEventCommitted(
            competitionId,
            Guid.CreateVersion7(),
            CompetitionEventKind.CompetitionAudienceChanged,
            CompetitionEventLevel.Information,
            DateTimeOffset.UnixEpoch,
            CompetitionAudienceChangeKind.AccessMode,
            CompetitionAccessMode.StaffOnly);

        await new LocalCompetitionEventMessageHandler(
                harness.Audiences,
                harness.Coordinator)
            .Handle(message, CancellationToken.None);

        _ = harness.Audiences.Received(1).AllKnownAsync(competitionId, CancellationToken.None);
        await harness.Coordinator.Received(1).ApplyAsync(message, CancellationToken.None);
    }

    private static PublisherHarness CreateHarness()
    {
        var context = Substitute.For<IHubContext<CompetitionHub, ICompetitionHubClient>>();
        var clients = Substitute.For<IHubClients<ICompetitionHubClient>>();
        var client = Substitute.For<ICompetitionHubClient>();
        var audiences = Substitute.For<ICompetitionHubAudienceRouter>();
        var coordinator = Substitute.For<ICompetitionHubAudienceCoordinator>();
        context.Clients.Returns(clients);
        clients.User(Arg.Any<string>()).Returns(client);
        clients.Clients(Arg.Any<IReadOnlyList<string>>()).Returns(client);
        clients.Group(Arg.Any<string>()).Returns(client);
        audiences.CurrentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(client);
        audiences.AllKnownAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(client);
        return new(context, clients, client, audiences, coordinator);
    }

    private sealed record PublisherHarness(
        IHubContext<CompetitionHub, ICompetitionHubClient> Context,
        IHubClients<ICompetitionHubClient> Clients,
        ICompetitionHubClient Client,
        ICompetitionHubAudienceRouter Audiences,
        ICompetitionHubAudienceCoordinator Coordinator);
}

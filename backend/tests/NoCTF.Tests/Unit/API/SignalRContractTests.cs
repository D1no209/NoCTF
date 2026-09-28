using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Endpoints.Competitions.Events;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Notifications;

namespace NoCTF.Tests.Unit.API;

public sealed class SignalRContractTests
{
    [Test]
    public async Task Client_interfaces_preserve_the_existing_wire_method_names()
    {
        var methods = typeof(ICompetitionHubClient)
            .GetMethods()
            .Concat(typeof(IPlatformLogHubClient).GetMethods())
            .Concat(typeof(INotificationHubClient).GetMethods())
            .ToArray();

        await Assert.That(methods.Select(method =>
                method.GetCustomAttributes(typeof(HubMethodNameAttribute), inherit: false)
                    .Cast<HubMethodNameAttribute>()
                    .Single()
                    .Name))
            .IsEquivalentTo([
                "scoreboardUpdated",
                "competitionLifecycleChanged",
                "competitionEventChanged",
                "gameplayFactStateChanged",
                "platformLogReceived",
                "notificationChanged"
            ]);
        await Assert.That(methods.All(method => method.ReturnType == typeof(Task))).IsTrue();
        await Assert.That(methods.All(method =>
            method.GetParameters()[^1].ParameterType == typeof(CancellationToken))).IsTrue();
    }

    [Test]
    public async Task Realtime_protocol_uses_camel_case_properties_and_pascal_case_enums()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var competitionId = Guid.CreateVersion7();
        var lifecycle = JsonSerializer.Serialize(
            new CompetitionLifecycleChangedNotification(
                competitionId,
                CompetitionStatusProtocol.Running,
                CompetitionStatusProtocol.Finished,
                DateTimeOffset.UnixEpoch),
            options);
        var competitionEvent = JsonSerializer.Serialize(
            new CompetitionEventChangedNotification(
                competitionId,
                Guid.CreateVersion7(),
                CompetitionEventKindProtocol.ScoringRecorded,
                CompetitionEventLevelProtocol.Information,
                DateTimeOffset.UnixEpoch),
            options);
        var scoreboard = JsonSerializer.Serialize(
            new ScoreboardUpdated(competitionId, "9007199254740993", "7", "11"),
            options);

        await Assert.That(lifecycle).Contains($"\"competitionId\":\"{competitionId}\"");
        await Assert.That(lifecycle).Contains("\"from\":\"Running\"");
        await Assert.That(lifecycle).Contains("\"to\":\"Finished\"");
        await Assert.That(competitionEvent).Contains("\"kind\":\"ScoringRecorded\"");
        await Assert.That(competitionEvent).Contains("\"level\":\"Information\"");
        await Assert.That(scoreboard).Contains("\"version\":\"9007199254740993\"");
        await Assert.That(scoreboard).Contains("\"schemaRevision\":\"7\"");
        await Assert.That(scoreboard).Contains("\"challengeCatalogRevision\":\"11\"");
    }

    [Test]
    public async Task Notification_hub_requires_authentication_and_accepts_realtime_bearer_tokens()
    {
        await Assert.That(typeof(NotificationHub)
            .IsDefined(typeof(AuthorizeAttribute), inherit: true)).IsTrue();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md")))
            root = root.Parent;
        await Assert.That(root).IsNotNull();
        var authentication = await File.ReadAllTextAsync(Path.Combine(root!.FullName,
            "backend", "src", "NoCTF.API", "Composition", "AuthenticationRegistration.cs"));
        await Assert.That(authentication).Contains("/hubs/v1/notifications");
    }
}

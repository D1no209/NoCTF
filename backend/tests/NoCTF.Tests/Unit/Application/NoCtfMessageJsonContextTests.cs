using System.Text.Json;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;

namespace NoCTF.Tests.Unit.Application;

public sealed class NoCtfMessageJsonContextTests
{
    [Test]
    public async Task Source_generated_realtime_messages_keep_the_current_wire_shape()
    {
        var competitionEvent = new CompetitionEventCommitted(
            Guid.NewGuid(), Guid.NewGuid(), default, default, DateTimeOffset.UnixEpoch);
        var fact = new GameplayFactStateChangedNotification(
            Guid.NewGuid(),
            new GameplayFactStatusView(Guid.NewGuid(), Guid.NewGuid(), null,
                Guid.NewGuid(), default, default, null, null,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        var scoreboard = new ScoreboardUpdated(Guid.NewGuid(), "1", "2", "3");

        await Assert.That(JsonSerializer.Serialize(competitionEvent,
                NoCtfMessageJsonContext.Default.CompetitionEventCommitted))
            .IsEqualTo(JsonSerializer.Serialize(competitionEvent));
        await Assert.That(JsonSerializer.Serialize(fact,
                NoCtfMessageJsonContext.Default.GameplayFactStateChangedNotification))
            .IsEqualTo(JsonSerializer.Serialize(fact));
        await Assert.That(JsonSerializer.Serialize(scoreboard,
                NoCtfMessageJsonContext.Default.ScoreboardUpdated))
            .IsEqualTo(JsonSerializer.Serialize(scoreboard));

        var platformLog = new PlatformLogView("cursor", DateTimeOffset.UnixEpoch,
            PlatformLogService.Host, PlatformLogLevel.Information, "NoCTF.Tests", 7,
            null, "ready", null, null, null, null, null, null, null, null);
        await Assert.That(JsonSerializer.Serialize(platformLog,
                NoCtfWebMessageJsonContext.Default.PlatformLogView))
            .IsEqualTo(JsonSerializer.Serialize(platformLog,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}

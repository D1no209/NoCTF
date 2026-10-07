using System.Text.Json;
using NoCTF.Domain.Competitions.StaffWebhooks;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Competitions.StaffWebhooks;

namespace NoCTF.Tests.Unit.Infrastructure;

[Category("StaffWebhooks")]
public sealed class StaffWebhookProtocolTests
{
    [Test]
    public async Task Staff_event_uses_distinct_source_and_only_allowlisted_summary()
    {
        var competitionId = Guid.NewGuid(); var itemId = Guid.NewGuid(); var subscriptionId = Guid.NewGuid();
        var row = new StaffWebhookEvent { Id = Guid.NewGuid(), CompetitionId = competitionId, CompetitionTitle = "Draft event",
            Kind = StaffWebhookEventKind.WorkItemCreated, ChangeKind = StaffWorkItemChangeKind.Created, Sequence = 42, OccurredAt = DateTimeOffset.UtcNow,
            Items = [new() { Summary = new() { Kind = StaffWorkItemKind.CheatIncident, Id = itemId, CheatStatus = CheatIncidentStatus.Pending,
                RequiresStaffAction = true, ActionRequiredSince = DateTimeOffset.UtcNow, ReasonCode = GameplayFactFailureCode.ForeignTeamFlagDetected } }] };
        var body = StaffWebhookProtocol.Serialize(row, subscriptionId, new("https://noctf.example/"));
        using var json = JsonDocument.Parse(body);
        await Assert.That(json.RootElement.GetProperty("source").GetString()).IsEqualTo($"https://noctf.example/api/v1/competitions/{competitionId}/staff-events");
        var item = json.RootElement.GetProperty("data").GetProperty("item");
        await Assert.That(item.GetProperty("status").GetString()).IsEqualTo("Pending");
        await Assert.That(item.TryGetProperty("value", out _)).IsFalse();
        await Assert.That(item.TryGetProperty("body", out _)).IsFalse();
        await Assert.That(item.TryGetProperty("sourceIpAddress", out _)).IsFalse();
        await Assert.That(item.TryGetProperty("relatedTeam", out _)).IsFalse();
    }

    [Test]
    public async Task Empty_snapshot_preserves_watermark_and_reason()
    {
        var row = new StaffWebhookEvent { Id = Guid.NewGuid(), CompetitionId = Guid.NewGuid(), Kind = StaffWebhookEventKind.PendingSnapshot,
            Sequence = 8, AsOfSequence = 7, SnapshotReason = StaffSnapshotReason.Resync, SnapshotId = Guid.NewGuid(), PageIndex = 0, PageCount = 1 };
        using var json = JsonDocument.Parse(StaffWebhookProtocol.Serialize(row, Guid.NewGuid(), new("https://noctf.example/")));
        var data = json.RootElement.GetProperty("data");
        await Assert.That(data.GetProperty("items").GetArrayLength()).IsEqualTo(0);
        await Assert.That(data.GetProperty("asOfSequence").GetInt64()).IsEqualTo(7L);
        await Assert.That(data.GetProperty("snapshotReason").GetString()).IsEqualTo("Resync");
        await Assert.That(data.GetProperty("pendingUrl").GetString()!.StartsWith("https://noctf.example/", StringComparison.Ordinal)).IsTrue();
    }
}

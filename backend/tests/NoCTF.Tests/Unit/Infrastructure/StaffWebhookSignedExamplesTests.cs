using System.Text;
using System.Text.Json;
using NoCTF.Domain.Competitions.StaffWebhooks;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.StaffWebhooks;
using NoCTF.Infrastructure.Competitions.Webhooks;

namespace NoCTF.Tests.Unit.Infrastructure;

[Category("StaffWebhooks")]
public sealed class StaffWebhookSignedExamplesTests
{
    [Test]
    public async Task All_event_variants_export_fictional_exact_body_signed_examples()
    {
        var now = DateTimeOffset.Parse("2026-10-07T06:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var competition = Guid.Parse("01990000-0000-7000-8000-000000000001");
        var target = Guid.Parse("01990000-0000-7000-8000-000000000002");
        var origin = new Uri("https://noctf.example.test/");
        var secret = "whsec_" + Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var events = new List<(string Name, StaffWebhookEvent Event)>();
        long sequence = 0;
        foreach (var kind in Enum.GetValues<StaffWorkItemKind>())
        {
            var item = new StaffWorkItemSummary { Id = Guid.NewGuid(), Kind = kind, CreatedAt = now.AddMinutes(-1), UpdatedAt = now,
                CheatStatus = kind == StaffWorkItemKind.CheatIncident ? CheatIncidentStatus.Pending : null,
                ConsultationStatus = kind == StaffWorkItemKind.Consultation ? CompetitionQuestionStatus.Pending : null,
                AppealStatus = kind == StaffWorkItemKind.BanAppeal ? TeamBanAppealStatus.Submitted : null,
                RequiresStaffAction = true, ActionRequiredSince = now.AddMinutes(-1), TeamId = Guid.NewGuid(), TeamName = "Fictional Blue Team",
                ChallengeId = Guid.NewGuid(), ChallengeTitle = "Fictional puzzle", Direction = "Web", Subject = kind == StaffWorkItemKind.Consultation ? CompetitionQuestionSubject.Challenge : null };
            var created = New(StaffWebhookEventKind.WorkItemCreated); created.ChangeKind = StaffWorkItemChangeKind.Created;
            item.LastChangedSequence = created.Sequence; created.Items = [new() { Summary = item.Copy() }]; events.Add(($"{kind}-created", created));
            var updated = New(StaffWebhookEventKind.WorkItemUpdated); updated.ChangeKind = StaffWorkItemChangeKind.StatusChanged;
            item.RequiresStaffAction = false; item.ActionRequiredSince = null; item.LastChangedSequence = updated.Sequence;
            if (kind == StaffWorkItemKind.CheatIncident) item.CheatStatus = CheatIncidentStatus.Confirmed;
            if (kind == StaffWorkItemKind.Consultation) { item.ConsultationStatus = CompetitionQuestionStatus.Replied; updated.ChangeKind = StaffWorkItemChangeKind.StaffReply; }
            if (kind == StaffWorkItemKind.BanAppeal) item.AppealStatus = TeamBanAppealStatus.Accepted;
            updated.Items = [new() { Summary = item.Copy() }]; events.Add(($"{kind}-updated", updated));
        }
        var empty = New(StaffWebhookEventKind.PendingSnapshot); empty.SnapshotId = Guid.NewGuid(); empty.AsOfSequence = 6;
        empty.SnapshotReason = StaffSnapshotReason.Initial; empty.PageIndex = 0; empty.PageCount = 1; events.Add(("empty-snapshot", empty));
        var snapshot = Guid.NewGuid();
        for (var page = 0; page < 2; page++)
        {
            var row = New(StaffWebhookEventKind.PendingSnapshot); row.SnapshotId = snapshot; row.AsOfSequence = 6;
            row.SnapshotReason = StaffSnapshotReason.Resync; row.PageIndex = page; row.PageCount = 2;
            row.Items = [new() { Summary = new() { Kind = StaffWorkItemKind.Consultation, Id = Guid.NewGuid(),
                ConsultationStatus = CompetitionQuestionStatus.Pending, RequiresStaffAction = true, CreatedAt = now.AddMinutes(-2), UpdatedAt = now,
                ActionRequiredSince = now.AddMinutes(-2), LastChangedSequence = 5 + page, TeamName = "Fictional Team" } }];
            events.Add(($"multi-page-snapshot-{page}", row));
        }
        foreach (var kind in new[] { StaffWebhookEventKind.Heartbeat, StaffWebhookEventKind.SubscriptionDisabled, StaffWebhookEventKind.Test })
            events.Add((kind.ToString(), New(kind)));
        var examples = events.Select(value =>
        {
            var body = StaffWebhookProtocol.Serialize(value.Event, target, origin);
            return new { value.Name, Headers = new Dictionary<string, string> { ["webhook-id"] = value.Event.Id.ToString(),
                ["webhook-timestamp"] = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["webhook-signature"] = CompetitionWebhookSender.Sign(value.Event.Id.ToString(), now.ToUnixTimeSeconds(), body, secret) }, RawBody = Encoding.UTF8.GetString(body) };
        }).ToArray();
        await Assert.That(examples.Length).IsEqualTo(12);
        foreach (var example in examples) await Assert.That(Encoding.UTF8.GetByteCount(example.RawBody) < 1024 * 1024).IsTrue();
        var destination = Environment.GetEnvironmentVariable("NOCTF_STAFF_CONTRACT_SAMPLES_OUTPUT");
        if (!string.IsNullOrEmpty(destination))
            await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(new { DemoSigningSecret = secret, CapturedAt = now,
                CompetitionId = competition, TargetId = target, Origin = origin.AbsoluteUri, Examples = examples }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        StaffWebhookEvent New(StaffWebhookEventKind kind) => new() { Id = Guid.NewGuid(), CompetitionId = competition, CompetitionTitle = "Fictional CTF",
            Sequence = ++sequence, LatestBusinessSequence = 6, Kind = kind, OccurredAt = now };
    }
}

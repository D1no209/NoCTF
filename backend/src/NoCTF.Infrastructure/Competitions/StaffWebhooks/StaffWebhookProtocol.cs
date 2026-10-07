using System.Text.Json;
using System.Text.Json.Serialization;
using NoCTF.Domain.Competitions.StaffWebhooks;

namespace NoCTF.Infrastructure.Competitions.StaffWebhooks;

public static class StaffWebhookProtocol
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = { new JsonStringEnumConverter() } };
    public static string Type(StaffWebhookEventKind kind) => kind switch
    {
        StaffWebhookEventKind.WorkItemCreated => "com.noctf.staff.work-item.created.v1",
        StaffWebhookEventKind.WorkItemUpdated => "com.noctf.staff.work-item.updated.v1",
        StaffWebhookEventKind.PendingSnapshot => "com.noctf.staff.pending.snapshot.v1",
        StaffWebhookEventKind.Heartbeat => "com.noctf.staff.heartbeat.v1",
        StaffWebhookEventKind.SubscriptionDisabled => "com.noctf.staff.subscription.disabled.v1",
        StaffWebhookEventKind.Test => "com.noctf.staff.test.v1",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public static byte[] Serialize(StaffWebhookEvent value, Guid subscriptionId, Uri origin)
    {
        if (origin.Scheme != "https") throw new InvalidOperationException("Staff webhook links require an HTTPS public origin.");
        var competition = new { Id = value.CompetitionId, Title = value.CompetitionTitle };
        var items = value.Items.OrderBy(item => item.Position).Select(item => Project(item.Summary, value.CompetitionId, origin)).ToArray();
        object data = value.Kind switch
        {
            StaffWebhookEventKind.WorkItemCreated or StaffWebhookEventKind.WorkItemUpdated =>
                new { SubscriptionId = subscriptionId, Competition = competition, value.Sequence, value.ChangeKind, Item = items.Single() },
            StaffWebhookEventKind.PendingSnapshot => new { SubscriptionId = subscriptionId, Competition = competition, value.Sequence,
                value.SnapshotId, value.SnapshotReason, value.AsOfSequence, value.PageIndex, value.PageCount, Items = items,
                PendingUrl = new Uri(origin, $"competitions/{value.CompetitionId}/staff").AbsoluteUri },
            StaffWebhookEventKind.Heartbeat => new { SubscriptionId = subscriptionId, Competition = competition, value.Sequence,
                CapturedAt = value.OccurredAt, value.LatestBusinessSequence },
            StaffWebhookEventKind.SubscriptionDisabled => new { SubscriptionId = subscriptionId, Competition = competition, value.Sequence, DisabledAt = value.OccurredAt },
            StaffWebhookEventKind.Test => new { SubscriptionId = subscriptionId, Competition = competition, value.Sequence },
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
        var item = value.Items.FirstOrDefault()?.Summary;
        return JsonSerializer.SerializeToUtf8Bytes(new { Specversion = "1.0", value.Id,
            Source = new Uri(origin, $"api/v1/competitions/{value.CompetitionId}/staff-events").AbsoluteUri, Type = Type(value.Kind),
            Subject = item is not null && value.Kind is StaffWebhookEventKind.WorkItemCreated or StaffWebhookEventKind.WorkItemUpdated
                ? $"{item.Kind}/{item.Id}" : null, Time = value.OccurredAt, Datacontenttype = "application/json",
            Dataschema = new Uri(origin, "schemas/webhooks/staff-events-v1.schema.json").AbsoluteUri, Data = data }, Options);
    }
    private static object Project(StaffWorkItemSummary value, Guid competitionId, Uri origin) => new
    {
        value.Kind, value.Id,
        Status = value.Kind switch { StaffWorkItemKind.CheatIncident => value.CheatStatus!.Value.ToString(),
            StaffWorkItemKind.Consultation => value.ConsultationStatus!.Value.ToString(), StaffWorkItemKind.BanAppeal => value.AppealStatus!.Value.ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(value)) },
        value.RequiresStaffAction, value.ActionRequiredSince, value.CreatedAt, value.UpdatedAt, value.DetectedAt,
        Team = value.TeamId is { } teamId ? new { Id = teamId, DisplayName = value.TeamName } : null,
        RelatedTeam = value.RelatedTeamId is { } relatedId ? new { Id = relatedId, DisplayName = value.RelatedTeamName } : null,
        Challenge = value.ChallengeId is { } challengeId ? new { Id = challengeId, Title = value.ChallengeTitle, value.Direction } : null,
        value.ReasonCode, value.Subject, value.ActorDisplayName, value.LastChangedSequence,
        ManagementUrl = ManagementUrl(value.Kind, value.Id, competitionId, origin)
    };
    public static string ManagementUrl(StaffWorkItemKind kind, Guid id, Guid competitionId, Uri origin) => new Uri(origin, kind switch {
            StaffWorkItemKind.CheatIncident => $"competitions/{competitionId}/staff?kind=CheatIncident&incident={id}",
            StaffWorkItemKind.Consultation => $"competitions/{competitionId}/questions?question={id}",
            StaffWorkItemKind.BanAppeal => $"competitions/{competitionId}/staff?kind=BanAppeal&appeal={id}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)) }).AbsoluteUri;
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Competitions.StaffWebhooks;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.StaffWebhooks;

/// <summary>Captures allowlisted staff projections in the same transaction as the source mutation.</summary>
internal static class StaffWebhookEventCapture
{
    private static readonly CompetitionEventKind[] Kinds = [CompetitionEventKind.CheatIncidentDetected,
        CompetitionEventKind.CheatIncidentConfirmed, CompetitionEventKind.CheatIncidentDismissed,
        CompetitionEventKind.CheatIncidentSuperseded, CompetitionEventKind.CheatIncidentCorrected,
        CompetitionEventKind.TeamBanAppealSubmitted, CompetitionEventKind.TeamBanAppealAccepted, CompetitionEventKind.TeamBanAppealUpheld,
        CompetitionEventKind.TeamBanned, CompetitionEventKind.TeamUnbanned, CompetitionEventKind.TeamBanCorrectionPublished,
        CompetitionEventKind.QuestionOpened, CompetitionEventKind.QuestionReplied, CompetitionEventKind.QuestionStatusChanged,
        CompetitionEventKind.TeamUpdated, CompetitionEventKind.ChallengeUpdated, CompetitionEventKind.ChallengeDescriptionUpdated, CompetitionEventKind.CompetitionUpdated];
    public static Guid[] ChangedCompetitions(NoCtfDbContext db) => db.ChangeTracker.Entries<CompetitionEvent>()
        .Where(value => value.State == EntityState.Added && Kinds.Contains(value.Entity.Kind)).Select(value => value.Entity.CompetitionId)
        .Concat(db.ChangeTracker.Entries<GameplayFact>().Where(value => value.State is EntityState.Added or EntityState.Modified
            && (CheatIncidentFailures.IsIncident(value.Entity.FailureCode) || CheatIncidentFailures.IsIncident(value.OriginalValues.GetValue<GameplayFactFailureCode?>(nameof(GameplayFact.FailureCode)))))
            .Select(value => value.Entity.CompetitionId))
        .Concat(db.ChangeTracker.Entries<Notification>().Where(value => value.State == EntityState.Added
            && value.Entity.QuestionStatus != null && value.Entity.TargetType == NotificationTargetType.CompetitionCollaborators)
            .Select(value => value.Entity.TargetId))
        .Distinct().ToArray();

    public static async Task CaptureAsync(NoCtfDbContext db, Guid[] competitions, DateTimeOffset now, CancellationToken ct, bool force = false)
    {
        foreach (var id in competitions)
        {
            // Unsubscribed competitions do not build an unnecessary read model on every business write.
            // Initial/re-enabled subscriptions rebuild their baseline from authoritative facts.
            if (!force && !db.StaffWebhookTargets.Local.Any(value => value.CompetitionId == id && value.Enabled && !value.Deleted)
                && !await db.StaffWebhookTargets.AnyAsync(value => value.CompetitionId == id && value.Enabled && !value.Deleted, ct)) continue;
            var current = await StaffWorkItemProjection.ReadAsync(db, id, ct);
            if (current.Count == 0 && !await db.StaffWebhookWorkItems.AnyAsync(value => value.CompetitionId == id, ct)) continue;
            var stream = await StreamAsync(db, id, ct);
            var title = db.Competitions.Local.FirstOrDefault(value => value.Id == id)?.Title
                ?? await db.Competitions.IgnoreQueryFilters().Where(value => value.Id == id).Select(value => value.Title).SingleAsync(ct);
            var existing = await db.StaffWebhookWorkItems.Where(value => value.CompetitionId == id).ToDictionaryAsync(value => (value.Kind, value.ItemId), ct);
            var changedNodes = db.ChangeTracker.Entries<Notification>().Where(value => value.State == EntityState.Added
                    && value.Entity.TargetType == NotificationTargetType.CompetitionCollaborators && value.Entity.TargetId == id)
                .Select(value => value.Entity).ToArray();
            foreach (var item in current.OrderBy(value => value.Kind).ThenBy(value => value.Id))
            {
                existing.TryGetValue((item.Kind, item.Id), out var old);
                if (old is not null && Same(old.Summary, item)) continue;
                if (db.ChangeTracker.Entries<StaffWebhookEvent>().Any(value => value.State == EntityState.Added
                    && value.Entity.CompetitionId == id && value.Entity.Items.Any(child => child.Summary.Kind == item.Kind && child.Summary.Id == item.Id))) continue;
                var node = changedNodes.LastOrDefault(value => (value.ThreadRootId ?? value.Id) == item.Id);
                var change = old is null ? StaffWorkItemChangeKind.Created : item.Kind == StaffWorkItemKind.Consultation && node?.Kind == NotificationKind.Message
                    ? node.QuestionActorRole is NoCTF.Domain.Challenges.Questions.CompetitionQuestionParticipantRole.Asker or NoCTF.Domain.Challenges.Questions.CompetitionQuestionParticipantRole.Participant
                        ? StaffWorkItemChangeKind.ParticipantMessage : StaffWorkItemChangeKind.StaffReply
                    : old.Summary.CheatStatus != item.CheatStatus || old.Summary.ConsultationStatus != item.ConsultationStatus
                        || old.Summary.AppealStatus != item.AppealStatus || old.Summary.RequiresStaffAction != item.RequiresStaffAction
                        ? StaffWorkItemChangeKind.StatusChanged : StaffWorkItemChangeKind.MetadataChanged;
                if (item.Kind == StaffWorkItemKind.CheatIncident && item.RequiresStaffAction && old?.Summary.RequiresStaffAction == true)
                    item.ActionRequiredSince = old.Summary.ActionRequiredSince;
                var sequence = checked(++stream.Sequence); stream.LatestBusinessSequence = sequence; item.LastChangedSequence = sequence;
                var row = old ?? new StaffWebhookWorkItem { CompetitionId = id, Kind = item.Kind, ItemId = item.Id };
                row.Summary = item.Copy(); if (old is null) db.StaffWebhookWorkItems.Add(row);
                AddEvent(db, stream, change == StaffWorkItemChangeKind.Created ? StaffWebhookEventKind.WorkItemCreated : StaffWebhookEventKind.WorkItemUpdated,
                    now, change, [item.Copy()], sequence: sequence).CompetitionTitle = title;
            }
            var keys = current.Select(value => (value.Kind, value.Id)).ToHashSet();
            foreach (var old in existing.Values.Where(value => !keys.Contains((value.Kind, value.ItemId)) && value.Summary.RequiresStaffAction))
            {
                old.Summary.RequiresStaffAction = false; old.Summary.ActionRequiredSince = null; old.Summary.UpdatedAt = now;
                if (old.Kind == StaffWorkItemKind.CheatIncident) old.Summary.CheatStatus = CheatIncidentStatus.Superseded;
                old.Summary.LastChangedSequence = checked(++stream.Sequence); stream.LatestBusinessSequence = stream.Sequence;
                AddEvent(db, stream, StaffWebhookEventKind.WorkItemUpdated, now, StaffWorkItemChangeKind.StatusChanged, [old.Summary.Copy()], sequence: stream.Sequence).CompetitionTitle = title;
            }
        }
    }
    private static bool Same(StaffWorkItemSummary left, StaffWorkItemSummary right) => left.CheatStatus == right.CheatStatus
        && left.ConsultationStatus == right.ConsultationStatus && left.AppealStatus == right.AppealStatus
        && left.RequiresStaffAction == right.RequiresStaffAction && left.UpdatedAt == right.UpdatedAt
        && left.TeamName == right.TeamName && left.RelatedTeamName == right.RelatedTeamName
        && left.ChallengeTitle == right.ChallengeTitle && left.Direction == right.Direction && left.ReasonCode == right.ReasonCode;
    public static async Task<StaffWebhookStream> StreamAsync(NoCtfDbContext db, Guid id, CancellationToken ct)
    {
        var stream = db.StaffWebhookStreams.Local.FirstOrDefault(value => value.CompetitionId == id)
            ?? await db.StaffWebhookStreams.SingleOrDefaultAsync(value => value.CompetitionId == id, ct);
        if (stream is not null) return stream;
        stream = new() { CompetitionId = id }; db.StaffWebhookStreams.Add(stream); return stream;
    }
    public static StaffWebhookEvent AddEvent(NoCtfDbContext db, StaffWebhookStream stream, StaffWebhookEventKind kind,
        DateTimeOffset now, StaffWorkItemChangeKind? change = null, IReadOnlyList<StaffWorkItemSummary>? items = null,
        Guid? targetId = null, long? sequence = null)
    {
        var row = new StaffWebhookEvent { Id = Guid.CreateVersion7(), CompetitionId = stream.CompetitionId,
            Sequence = sequence ?? checked(++stream.Sequence), LatestBusinessSequence = stream.LatestBusinessSequence,
            Kind = kind, ChangeKind = change, OccurredAt = now, TargetId = targetId };
        row.Items = (items ?? []).Select((value, index) => new StaffWebhookEventItem { EventId = row.Id, Position = index, Summary = value.Copy() }).ToList();
        db.StaffWebhookEvents.Add(row); return row;
    }
}
